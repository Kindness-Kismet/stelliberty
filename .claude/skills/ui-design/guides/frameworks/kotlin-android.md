# Kotlin / Android 运行示例

复用 [Kotlin 公共组件](kotlin.md)，演示按钮、设置列表、标签、菜单、对话框与短 Toast。窗口使用真实 Compose 控件与输入路由，明暗主题和强调色可以在页面切换。

## 依赖与入口

以下配置对应已编译的 Android 示例：Gradle `9.9.1`、AGP `9.5.1`、Kotlin Compose compiler `2.4.21`、Compose UI/Foundation/Animation `1.12.1`、Material3 `1.5.0-rc01`、Activity Compose `1.13.0`。AGP 使用内置 Kotlin 支持；编译 SDK 为 `37.0`，最低 API `26`，JVM 目标 `21`。已有项目保留兼容的依赖与构建配置，AndroidX 版本分别声明，不套用 Compose Multiplatform 的统一版本号。

创建以下文件，并把 [公共组件代码](kotlin.md)保存为 `app/src/main/java/ui/design/examples/Components.kt`。

`settings.gradle.kts`：

```kotlin
pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "kotlin-ui-demo"
include(":app")
```

根目录 `build.gradle.kts`：

```kotlin
plugins {
    id("com.android.application") version "9.5.1" apply false
    id("org.jetbrains.kotlin.plugin.compose") version "2.4.21" apply false
}
```

`gradle.properties`：

```properties
org.gradle.jvmargs=-Xmx2048m -Dfile.encoding=UTF-8
android.useAndroidX=true
kotlin.code.style=official
```

`app/build.gradle.kts`：

```kotlin
plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "org.example.uidesign.kotlin.demo"
    compileSdk {
        version = release(37) { minorApiLevel = 0 }
    }
    defaultConfig {
        applicationId = "org.example.uidesign.kotlin.demo"
        minSdk = 26
        targetSdk = 37
        versionCode = 1
        versionName = "1.0"
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_21
        targetCompatibility = JavaVersion.VERSION_21
    }
    buildFeatures { compose = true }
}

dependencies {
    implementation("androidx.activity:activity-compose:1.13.0")
    implementation("androidx.compose.ui:ui:1.12.1")
    implementation("androidx.compose.foundation:foundation:1.12.1")
    implementation("androidx.compose.animation:animation:1.12.1")
    implementation("androidx.compose.material3:material3:1.5.0-rc01")
}
```

`app/src/main/AndroidManifest.xml`：

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
    <application android:theme="@android:style/Theme.Material.Light.NoActionBar"
        android:label="Kotlin 控件预览" android:allowBackup="false" android:supportsRtl="true">
        <activity android:name=".MainActivity" android:exported="true">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>
    </application>
</manifest>
```

`app/src/main/java/org/example/uidesign/kotlin/demo/MainActivity.kt`：

```kotlin
package org.example.uidesign.kotlin.demo

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.material3.MaterialTheme
import ui.design.examples.Gallery

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { MaterialTheme { Gallery() } }
    }
}
```

## 调用页面

保存为 `app/src/main/java/ui/design/examples/Gallery.kt`。菜单始终位于全尺寸根 Box，内容宽度放在内部 Column；关闭时保留 overlay，直到当前输入序列释放。菜单留白按当前密度取整后派生条目圆角，触发器位置换算到宿主坐标；Android 返回键与关闭后的控件焦点分别接入 BackHandler 和 FocusRequester；键盘模式恢复触发器焦点，触摸模式遵循组件库的焦点规则。

标签内容通过 active 禁用退出页的输入，动态结果使用简短 Toast。现有项目的业务与翻译文本替换对应示例回调与字符串。

```kotlin
package ui.design.examples

import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.compose.ui.unit.DpOffset
import androidx.compose.ui.unit.dp

@Composable
fun Gallery() {
    val context = LocalContext.current
    val density = LocalDensity.current
    var dark by remember { mutableStateOf(false) }
    var accentIndex by remember { mutableIntStateOf(0) }
    var menuExpanded by remember { mutableStateOf(false) }
    var dialogVisible by remember { mutableStateOf(false) }
    var autoConnect by remember { mutableStateOf(false) }
    var groupName by remember { mutableStateOf("") }
    var groups by remember { mutableIntStateOf(2) }
    var rootBounds by remember { mutableStateOf(Rect.Zero) }
    var menuAnchor by remember { mutableStateOf(Rect.Zero) }
    val menuFocus = remember { FocusRequester() }
    val accents = listOf(Color(0xffffdf38), Color(0xff8ed6ff), Color(0xff174ea6))
    val surface = if (dark) Color(0xff212121) else Color.White
    val foreground = if (dark) Color(0xfff5f5f5) else Color(0xff18263a)
    val tokens = UiTokens(
        surface = surface,
        foreground = foreground,
        accent = accents[accentIndex],
        rowTitle = foreground,
        rowSubtitle = if (dark) Color(0xffb7bec8) else Color(0xff626a73),
        isDark = dark,
        buttonRadius = 8.dp,
        buttonPadding = PaddingValues(horizontal = 16.dp, vertical = 12.dp),
        rowPadding = PaddingValues(horizontal = 12.dp, vertical = 12.dp),
    )
    val notify: (String) -> Unit = { Toast.makeText(context, it, Toast.LENGTH_SHORT).show() }
    val mark = RowAdornment(palette = { listOf(Color(0xff238fe8), Color.White) }) { colors ->
        Canvas(Modifier.padding(end = 12.dp).size(28.dp)) {
            drawCircle(colors[0])
            val arm = size.minDimension * 0.2f
            val stroke = size.minDimension * 0.08f
            drawLine(colors[1], center - Offset(arm, 0f), center + Offset(arm, 0f), stroke, StrokeCap.Round)
            drawLine(colors[1], center - Offset(0f, arm), center + Offset(0f, arm), stroke, StrokeCap.Round)
        }
    }

    MaterialTheme(colorScheme = if (dark) darkColorScheme() else lightColorScheme()) {
        BackHandler(enabled = menuExpanded) { menuExpanded = false }
        Box(Modifier.fillMaxSize().background(surface).safeDrawingPadding()
            .semantics { testTagsAsResourceId = true }
            .onGloballyPositioned { rootBounds = it.boundsInRoot() }) {
            Column(Modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Text("控件预览", Modifier.weight(1f), color = foreground, style = MaterialTheme.typography.titleLarge)
                    FeedbackButton(tokens, ButtonRole.Plain, { dark = !dark }, Modifier.testTag("theme")) {
                        Text(if (dark) "浅色" else "深色")
                    }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FeedbackButton(tokens, ButtonRole.Dismiss, { notify("已取消") }, Modifier.testTag("cancel")) {
                        Text("取消")
                    }
                    FeedbackButton(tokens, ButtonRole.Primary, { notify("已保存") }, Modifier.testTag("save")) {
                        Text("保存")
                    }
                    FeedbackButton(tokens, ButtonRole.Plain, { menuExpanded = true },
                        Modifier.testTag("menu.open").focusRequester(menuFocus)
                            .onGloballyPositioned { menuAnchor = it.boundsInRoot() }) {
                        Text("更多")
                    }
                }
                CrossfadeTabs(tokens, listOf(
                    TabPage("groups", "分组", { active ->
                        Column(Modifier.padding(top = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            Text("$groups 个分组", color = foreground)
                            FeedbackButton(tokens, ButtonRole.Primary, { dialogVisible = true },
                                Modifier.testTag("group.add"), enabled = active) { Text("创建分组") }
                        }
                    }),
                    TabPage("settings", "设置", { active ->
                        Column(Modifier.padding(top = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            FeedbackRowText(tokens, RowKind.SettingsCard, "自动连接", if (autoConnect) "已开启" else "已关闭",
                                { autoConnect = !autoConnect; notify(if (autoConnect) "已开启" else "已关闭") },
                                Modifier.testTag("connection"), leading = mark, enabled = active)
                            FeedbackButton(tokens, ButtonRole.Primary, { accentIndex = (accentIndex + 1) % accents.size },
                                Modifier.testTag("accent"), enabled = active) { Text("切换强调色") }
                        }
                    }),
                ), Modifier.weight(1f))
            }

            // overlay 始终留在根容器，关闭内容后继续吞掉本次释放。
            val menuWidth = 196.dp
            val offset = with(density) {
                DpOffset(
                    (menuAnchor.left - rootBounds.left).toDp().coerceIn(0.dp, (rootBounds.width.toDp() - menuWidth).coerceAtLeast(0.dp)),
                    (menuAnchor.bottom - rootBounds.top).toDp() + 8.dp,
                )
            }
            val inset = with(density) { 5.dp.roundToPx().toDp() }
            val rowRadius = (tokens.popupRadius - inset).coerceAtLeast(0.dp)
            RoundedMenu(tokens, menuExpanded, { menuExpanded = false }, offset, returnFocus = menuFocus) {
                Column(Modifier.width(menuWidth).padding(inset)) {
                    FeedbackRowText(tokens, RowKind.Menu, "创建分组", null,
                        { menuExpanded = false; dialogVisible = true }, Modifier.testTag("menu.create").heightIn(min = 48.dp),
                        CornerContext.GroupCap(rowRadius))
                    FeedbackRowText(tokens, RowKind.Menu, "切换强调色", null,
                        { accentIndex = (accentIndex + 1) % accents.size; menuExpanded = false; notify("已切换") },
                        Modifier.testTag("menu.accent").heightIn(min = 48.dp), CornerContext.GroupCap(rowRadius))
                }
            }
        }

        if (dialogVisible) {
            RoundedDialog(tokens, { dialogVisible = false }) {
                Column(Modifier.width(300.dp).padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    Text("创建分组", color = foreground, style = MaterialTheme.typography.titleLarge)
                    OutlinedTextField(groupName, { groupName = it }, Modifier.fillMaxWidth().testTag("group.name"),
                        label = { Text("名称") }, singleLine = true)
                    Row(Modifier.align(Alignment.End), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        FeedbackButton(tokens, ButtonRole.Dismiss, { dialogVisible = false }, Modifier.testTag("dialog.cancel")) {
                            Text("取消")
                        }
                        FeedbackButton(tokens, ButtonRole.Primary, {
                            groups += 1
                            groupName = ""
                            dialogVisible = false
                            notify("已创建")
                        }, Modifier.testTag("dialog.create"), enabled = groupName.isNotBlank()) { Text("创建") }
                    }
                }
            }
        }
    }
}
```

## 构建与验证

安装对应 SDK，将 `ANDROID_HOME` 指向 SDK 目录，使用 Gradle `9.9.1` 或已有工程的兼容 wrapper：

```text
gradle :app:assembleDebug --max-workers 12
adb -s <device-id> install -r app/build/outputs/apk/debug/app-debug.apk
adb -s <device-id> shell am start -n org.example.uidesign.kotlin.demo/.MainActivity
```

验收真实输入流程与截图：按钮按下/释放和取消、触屏列表按压、鼠标悬停、文字标签切换、菜单外点击吞完整序列、返回键与焦点恢复、输入名称及创建分组、明暗主题和强调色文字对比。自动化验证前，按项目调试 skill 接入应用内输入通道。模拟不占用电脑键鼠；需要抢占用户输入或前台焦点时先询问并取得明确同意。
