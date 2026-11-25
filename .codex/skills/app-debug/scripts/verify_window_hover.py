import ctypes
from ctypes import wintypes
import json
import os
from datetime import datetime
from PIL import Image, ImageGrab
import win32gui
import win32ui
import time


def run(cli, assert_cleared=False):
    if os.name != "nt":
        raise RuntimeError("Native hover verification requires Windows")
    cli.ensure_debug_app()
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.SetThreadDpiAwarenessContext.argtypes = [wintypes.HANDLE]
    user32.SetThreadDpiAwarenessContext.restype = wintypes.HANDLE
    dpi_context = user32.SetThreadDpiAwarenessContext(-4)
    user32.GetCursorPos.argtypes = [ctypes.POINTER(wintypes.POINT)]
    user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    user32.mouse_event.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_size_t]
    user32.WindowFromPoint.argtypes = [wintypes.POINT]
    user32.WindowFromPoint.restype = wintypes.HWND
    user32.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetAncestor.restype = wintypes.HWND
    original_cursor = wintypes.POINT()
    if not user32.GetCursorPos(ctypes.byref(original_cursor)):
        raise ctypes.WinError(ctypes.get_last_error())
    original = dict(part.split("=", 1) for part in cli.send_command("settings.app-behavior.state").split(";") if "=" in part)

    def state(automation_id="TitleBar.CloseButton"):
        return json.loads(cli.send_command(f"control.get pointer {automation_id}"))

    def native_crop(current):
        handle = current["WindowHandle"]
        left, top, right, bottom = win32gui.GetWindowRect(handle)
        native_dc = win32gui.GetWindowDC(handle)
        source_dc = win32ui.CreateDCFromHandle(native_dc)
        memory_dc = source_dc.CreateCompatibleDC()
        bitmap = win32ui.CreateBitmap()
        bitmap.CreateCompatibleBitmap(source_dc, right - left, bottom - top)
        previous = memory_dc.SelectObject(bitmap)
        try:
            user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
            if not user32.PrintWindow(handle, memory_dc.GetSafeHdc(), 2):
                raise ctypes.WinError(ctypes.get_last_error())
            picture = Image.frombuffer("RGB", (right - left, bottom - top), bitmap.GetBitmapBits(True), "raw", "BGRX", 0, 1)
            return picture.crop((current["X"] - left - 33, current["Y"] - top - 25, current["X"] - left + 33, current["Y"] - top + 25))
        finally:
            memory_dc.SelectObject(previous)
            win32gui.DeleteObject(bitmap.GetHandle())
            memory_dc.DeleteDC()
            source_dc.DeleteDC()
            win32gui.ReleaseDC(handle, native_dc)

    def emit(stage, current):
        picture = ImageGrab.grab(bbox=(current["X"] - 33, current["Y"] - 25, current["X"] + 33, current["Y"] + 25), all_screens=True)
        pixels = list(picture.convert("RGB").getdata())
        current["ScreenRedPixels"] = sum(r > 160 and g < 90 and b < 90 for r, g, b in pixels)
        picture_path = cli.SCREENSHOT_DIR / f"hover-{datetime.now():%Y%m%d-%H%M%S-%f}-{stage}.png"
        picture.save(picture_path)
        current["ScreenCrop"] = str(picture_path)
        native = native_crop(current)
        current["NativeRedPixels"] = sum(r > 160 and g < 90 and b < 90 for r, g, b in native.getdata())
        native_path = picture_path.with_name(picture_path.stem + "-native.png")
        native.save(native_path)
        current["NativeCrop"] = str(native_path)
        print(json.dumps({"stage": stage, **current}, ensure_ascii=False), flush=True)

    def require_clear(stage, current):
        emit(stage, current)
        if assert_cleared and (current["IsPointerOver"] or current["IsPressed"]
                               or (current["WindowVisible"] and current["ScreenRedPixels"] > 100)):
            raise RuntimeError(f"Stale pointer state at {stage}")

    try:
        cli.send_command("settings.app-behavior.set minimize-to-tray true")
        cli.send_command("window.show")
        time.sleep(0.3)
        neutral = state("TitleBar.Root")
        for label, hidden_duration in [("quick", 0.15), ("released", 6)]:
            target = state()
            user32.SetCursorPos(neutral["X"], neutral["Y"])
            time.sleep(0.08)
            user32.SetCursorPos(target["X"], target["Y"])
            time.sleep(0.15)
            hovered = state()
            emit(f"{label}-hovered", hovered)
            if not hovered["WindowVisible"] or not hovered["IsPointerOver"] or user32.GetAncestor(user32.WindowFromPoint(wintypes.POINT(target["X"], target["Y"])), 2) != target["WindowHandle"]:
                raise RuntimeError("The expected visible close button is not the mouse target")
            # 鼠标位置取自 AutomationId，点击前确认指针下的窗口属于当前应用。
            user32.mouse_event(0x0002, 0, 0, 0, 0)
            time.sleep(0.04)
            user32.mouse_event(0x0004, 0, 0, 0, 0)
            time.sleep(0.1)
            hidden = state()
            require_clear(f"{label}-hidden", hidden)
            if hidden["WindowVisible"]:
                raise RuntimeError("Clicking the close button did not hide the window")
            user32.SetCursorPos(neutral["X"], neutral["Y"])
            time.sleep(hidden_duration)
            cli.send_command("window.show")
            require_clear(f"{label}-shown", state())
            previous = cli.latest_screenshot()
            cli.send_command("screenshot.take")
            print(f"screenshot={cli.wait_for_new_screenshot(previous)}", flush=True)
        target = state()
        user32.SetCursorPos(neutral["X"], neutral["Y"])
        time.sleep(0.15)
        user32.SetCursorPos(target["X"], target["Y"])
        time.sleep(0.15)
        hovered = state()
        emit("hover-restored", hovered)
        if not hovered["IsPointerOver"]:
            raise RuntimeError("The close button no longer responds to pointer entry")
        user32.SetCursorPos(neutral["X"], neutral["Y"])
        time.sleep(0.1)
        require_clear("pointer-left", state())
    finally:
        user32.mouse_event(0x0004, 0, 0, 0, 0)
        cli.send_command("window.show")
        cli.send_command(f"settings.app-behavior.set minimize-to-tray {original['minimizeToTray']}")
        user32.SetCursorPos(original_cursor.x, original_cursor.y)
        user32.SetThreadDpiAwarenessContext(dpi_context)
