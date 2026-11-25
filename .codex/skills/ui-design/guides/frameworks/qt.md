# Qt Widgets

已有组件库优先使用其 style 与 interaction，先核对已有动画再补缺失反馈；普通按钮的微缩放复用 FeedbackEffect 与 FeedbackBinder，复合条目的局部图标反馈复用 IconFeedback。需要完整自绘组件时使用 Action，共用同一套 Tokens。

使用 Qt 6.11.2 的 QAbstractButton、QVariantAnimation、QGraphicsEffect、QTabBar 和 QStackedWidget。组件在 GUI 线程创建，由 QWidget 父对象管理；代码不需要额外的 Q_OBJECT 或 moc 文件。

- Tokens 接收项目实际主题，默认 accent 为 #238fe8，纯黑为 #000000，纯白为 #FFFFFF。surface 必须是最终不透明背景，accent 可以带透明度；先合成 accent，再以线性化 sRGB 相对亮度为每帧 Filled 操作按钮选择对比度较高的黑白文字。Plain 使用主题 foreground，背景透明且没有悬停底色。
- mix 使用 sRGB 编码通道 `base * (1 - alpha) + overlay * alpha`，结果按 8 位通道四舍五入，透明 accent 的 alpha 使用同一公式；对比度计算单独线性化 sRGB。
- Dismiss 使用 surface 向 bestText(surface) 混合 8% 的中性底色；Primary 使用合成后的 accent，悬停再向该底色的 bestText 混合 8%。2种 Filled 按钮的文字对比度至少 4.5:1。
- Navigation 行使用独立圆角 10 和内缩 8、2、8、2 的悬停底；Menu 行使用独立圆角 7 和内缩 6、1、6、1 的悬停底。Card 使用整体卡片底，cardHover 从主题传入，浅色为 #eff2f7、深色为 #2d2d2d。列表标题与副标题保留原始主题颜色，副标题自动换行；单行 Navigation 至少 40，带副标题时至少 58 并随内容增高，Card 至少 58，Menu 至少 36。按压 level 从 0 到 1，背景通过 feedbackColor 轻微加深，leading/trailing QIcon 通过 IconFeedback::paint 单独加深，brightness=.92、contrast=1.12，按压 30 ms、释放 72 ms。行组件的 graphicsEffect 保持为空，局部滤色的图像输入限定为单个图标像素。
- RoundedAncestor 明确最近圆角祖先及真实内缩 d，d 包括 border、padding、绘制内缩，文字内缩和投影不参与。实际邻接角的横纵内缩相等时，通过 equalInsetAncestor 与 CornerContext::Adjacent 按 `max(R - d, 0)` 派生指定角；不等距时，调用方先调整宿主布局和 setPaintInset，使实际绘制边界等距，再提供上下文。确实没有圆角祖先时使用独立圆角，中间行用 CornerContext::GroupCap 显式共享组圆角。makeActions 的首末行显式提供统一 paint inset，其值与组 padding 一起组成 d，分别派生首行顶角、末行底角；圆角路径裁在可用尺寸之内。
- Spring 只改变普通按钮绘制比例，原命中区域保持不变。鼠标拖出、重新进入、键盘激活、失焦、隐藏与禁用沿 QAbstractButton 的实际状态处理；背景颜色与局部图标使用同一预乘 alpha 通道公式，RGB 不超过 alpha，文字独立绘制。
- 文字型 Tab 包含纯文字和图标加文字，hover 使用 Tokens::textTabHover，Navigation/Menu 使用独立的 navigationHover。tokensForSurface 在共享主题 surface 上叠 8% 的高对比纯黑或纯白，派生同一个默认颜色值并分别保存到2个字段；已有组件库优先保留导航和文字 Tab 各自的语义键，2个字段可分别覆盖。局部控件改变 surface 时保留主题 hover 字段。当前业务选中的文字 Tab 按 currentIndex() 抑制 hover 底色，未选中项正常显示；pressed 对自己的 raw hover 做 brightness=.92、contrast=1.12 的轻微加深，混合型图标通过 IconFeedback 局部反馈。背景内缩 4、圆角 7，hover 立即变化，按压 30 ms、释放 72 ms；纯图标型保持原视觉且不接这些新增反馈。文字按原选中权重和实际背景对比度算法独立绘制，weight 大于 0 的最终不透明文字核对 4.5:1，不足时选黑白中对比度较高者。指示线的独立不透明基色按实际下方 surface 核对 3:1，厚度 2、圆头半径 1，显示 alpha 随权重变化。鼠标在同一标签内释放才选择，移出后释放取消；键盘箭头交给 QTabBar，Space/Enter 保留按压反馈。
- 选中权重和内容切换使用 160 ms linear。内容切换用2个不透明快照交叉淡化，连续切换从当前合成帧接续；遮罩关闭旧内容输入，标签栏自身的键盘焦点保持可用。Tokens::animationSpeed 接收有限且大于 0 的实数，effectiveDuration 统一将时长换算成 `base_ms / speed`，适用于 Spring、Luminance 和 Tab 动画。
- RoundedSurface 的透明外层容纳投影，RoundedBody 容纳背景和子控件，RoundedClipEffect 将整个 body 的子树裁成圆角，避免子控件穿出。外层使用模糊 5、偏移 (0,1)、颜色 rgba(0,0,0,0.25) 的浮层投影，圆角按 Dialog/Menu/Dropdown 选择 16/12/8；投影留白依据 QGraphicsDropShadowEffect::boundingRectFor 计算。按钮、列表行和标签不设置投影。Qt 的 blur 与 offset 按 QGraphicsDropShadowEffect 的真实坐标语义处理，DPI 接入时核对外扩与缓存边界；原生窗口投影继续交给平台。
- MenuDismissLayer 是菜单所在 host 的透明顶层屏蔽层，showAt 的矩形包含菜单外层，命中按 body 的圆角轮廓计算。应用级事件过滤在子控件处理前截住圆角外的按压，隐藏菜单后吞掉 press、move、匹配 release 以及触摸整个序列，序列结束才隐藏屏蔽层。Escape 按下关闭、释放后移除屏蔽；窗口失活、host 隐藏、丢失鼠标捕获和 TouchCancel 清理序列。菜单项业务完成时调用 closeMenu，host 改变尺寸时屏蔽层同步尺寸。

FeedbackBinder::attach 为已有普通操作按钮接入微缩放，使用其真实动作和鼠标状态；已有 graphicsEffect 时返回 nullptr，由调用方在原绘制层组合效果。已有复合行在原绘制中使用 IconFeedback::paint 绘制装饰图标，状态变化调用 change，取消调用 reset，背景取 feedbackColor(raw, level, tokens)。Checkbox、Toggle 等独立 QWidget 保留自己的命中、状态与反馈，QIcon 参数只表示非交互装饰。

将组件的 clicked/currentChanged 连接到实际用例；页面添加并完成非零布局后调用 CrossfadeStack::setIndex。1个 QWidget 只有1个 graphicsEffect，复用组件时保留这项约束。

下面是1段自包含的完整 C++ 源码；按需要拆成公共参数、反馈效果与组件文件，保持相同依赖。makeActions 演示 Plain 查看日志、Primary 连接、Dismiss 取消和同组卡片行，bindTabs 连接标签与内容。主题改变时向现有组件传递更新后的 Tokens。

```cpp
#include <QtCore/QEvent>
#include <QtCore/QPointer>
#include <QtCore/QVariantAnimation>
#include <QtGui/QIcon>
#include <QtGui/QImage>
#include <QtGui/QEnterEvent>
#include <QtGui/QKeyEvent>
#include <QtGui/QMouseEvent>
#include <QtGui/QPainter>
#include <QtGui/QPainterPath>
#include <QtGui/QTouchEvent>
#include <QtWidgets/QAbstractButton>
#include <QtWidgets/QApplication>
#include <QtWidgets/QGraphicsDropShadowEffect>
#include <QtWidgets/QGraphicsEffect>
#include <QtWidgets/QStackedWidget>
#include <QtWidgets/QTabBar>
#include <QtWidgets/QVBoxLayout>

#include <algorithm>
#include <array>
#include <cmath>
#include <optional>
#include <utility>

namespace UiExample {

enum class ButtonRole { Plain, Dismiss, Primary };
enum class RowKind { None, Navigation, Card, Menu };
enum class SurfaceKind { Dialog, Menu, Dropdown };

struct Tokens {
    QColor surface = QColor("#ffffff");
    QColor foreground = QColor("#18263a");
    QColor secondary = QColor("#858c98");
    QColor accent = QColor("#238fe8");
    QColor navigationHover = QColor("#ebebeb");
    QColor textTabHover = QColor("#ebebeb");
    QColor cardHover = QColor("#eff2f7");
    qreal neutralMix = .08;
    qreal hoverMix = .08;
    qreal pressScale = .94;
    qreal brightnessDelta = .08;
    qreal contrastDelta = .12;
    qreal springOverscan = .01;
    int springMs = 180;
    int pressMs = 30;
    int releaseMs = 72;
    int tabsMs = 160;
    qreal buttonRadius = 8.;
    qreal rowRadius = 10.;
    qreal menuItemRadius = 7.;
    qreal dialogRadius = 16.;
    qreal menuRadius = 12.;
    qreal dropdownRadius = 8.;
    qreal shadowBlur = 5.;
    qreal shadowOpacity = .25;
    QPointF shadowOffset = QPointF(0., 1.);
    QMarginsF rowHoverInset = QMarginsF(8., 2., 8., 2.);
    QMarginsF menuHoverInset = QMarginsF(6., 1., 6., 1.);
    int buttonHeight = 36;
    int rowHeight = 58;
    int navigationHeight = 40;
    int menuHeight = 36;
    int rowVerticalInset = 10;
    int secondaryGap = 4;
    int textInset = 22;
    int iconSize = 20;
    int iconGap = 12;
    int frameInset = 1;
    qreal focusWidth = 1.5;
    int tabsIndicatorHeight = 2;
    int tabsInset = 4;
    qreal tabsRadius = 7.;
    qreal selectedTextContrast = 4.5;
    qreal indicatorContrast = 3.;
    qreal animationSpeed = 1.;
    int surfaceInset = 16;
};

constexpr int feedbackChannel(int value, int alpha, qreal level,
    qreal brightnessDelta, qreal contrastDelta) {
    return std::clamp(qRound((value * (1. - brightnessDelta * level) - alpha / 2.)
        * (1. + contrastDelta * level) + alpha / 2.), 0, alpha);
}

static_assert(feedbackChannel(235, 255, 1., .08, .12) == 227);
static_assert(feedbackChannel(128, 255, 1., .08, .12) == 117);
static_assert(feedbackChannel(64, 128, 1., .08, .12) == 58);
static_assert(feedbackChannel(64, 128, 0., .08, .12) == 64);

struct RoundedAncestor { qreal radius; qreal inset; };

enum class Corners : unsigned {
    TopLeft = 1, TopRight = 2, BottomRight = 4, BottomLeft = 8,
    Top = 3, Bottom = 12, All = 15
};

struct CornerContext {
    enum class Kind { None, Adjacent, GroupCap };
    Kind kind = Kind::None;
    Corners corners = Corners::All;
    std::optional<RoundedAncestor> ancestor;
    qreal groupRadius = 0.;
};

std::optional<RoundedAncestor> equalInsetAncestor(
    qreal radius, qreal horizontalInset, qreal verticalInset) {
    if (horizontalInset < 0. || verticalInset < 0.
        || !qFuzzyCompare(1. + horizontalInset, 1. + verticalInset)) {
        return std::nullopt;
    }
    return RoundedAncestor{radius, horizontalInset};
}

qreal derivedRadius(qreal standalone,
    const std::optional<RoundedAncestor> &ancestor) {
    return ancestor ? std::max(qreal(0.), ancestor->radius - ancestor->inset)
        : standalone;
}

int effectiveDuration(int milliseconds, qreal speed) {
    Q_ASSERT_X(std::isfinite(speed) && speed > 0.,
        "effectiveDuration", "Animation speed must be finite and positive");
    return std::max(1, qRound(milliseconds / speed));
}

QPainterPath roundedPath(QRectF area, std::array<qreal, 4> radii) {
    const auto maximum = std::max(qreal(0.),
        std::min(area.width(), area.height()) / 2.);
    for (auto &radius : radii) {
        radius = std::clamp(radius, qreal(0.), maximum);
    }
    const auto [tl, tr, br, bl] = radii;
    QPainterPath result;
    result.moveTo(area.left() + tl, area.top());
    result.lineTo(area.right() - tr, area.top());
    result.arcTo(QRectF(area.right() - 2. * tr, area.top(), 2. * tr, 2. * tr),
        90., -90.);
    result.lineTo(area.right(), area.bottom() - br);
    result.arcTo(QRectF(area.right() - 2. * br, area.bottom() - 2. * br,
        2. * br, 2. * br), 0., -90.);
    result.lineTo(area.left() + bl, area.bottom());
    result.arcTo(QRectF(area.left(), area.bottom() - 2. * bl, 2. * bl, 2. * bl),
        270., -90.);
    result.lineTo(area.left(), area.top() + tl);
    result.arcTo(QRectF(area.left(), area.top(), 2. * tl, 2. * tl), 180., -90.);
    result.closeSubpath();
    return result;
}

qreal linearChannel(qreal value) {
    return value <= .04045 ? value / 12.92
        : std::pow((value + .055) / 1.055, 2.4);
}

qreal luminance(QColor color) {
    return .2126 * linearChannel(color.redF())
        + .7152 * linearChannel(color.greenF())
        + .0722 * linearChannel(color.blueF());
}

QColor bestText(QColor opaqueBackground) {
    const auto light = luminance(opaqueBackground);
    return (light + .05) / .05 >= 1.05 / (light + .05)
        ? QColor(Qt::black) : QColor(Qt::white);
}

QColor mix(QColor from, QColor to, qreal amount) {
    const auto channel = [=](int a, int b) {
        return std::clamp(qRound(a * (1. - amount) + b * amount), 0, 255);
    };
    return QColor(channel(from.red(), to.red()),
        channel(from.green(), to.green()), channel(from.blue(), to.blue()));
}

QColor composite(QColor foreground, QColor opaqueSurface) {
    return mix(opaqueSurface, foreground, foreground.alphaF());
}

Tokens tokensForSurface(QColor sharedThemeSurface) {
    auto result = Tokens{};
    result.surface = sharedThemeSurface;
    result.navigationHover = mix(sharedThemeSurface, bestText(sharedThemeSurface), .08);
    result.textTabHover = result.navigationHover;
    return result;
}

QColor feedbackColor(QColor color, qreal level, const Tokens &tokens) {
    if (level == 0.) return color;
    const auto alpha = color.alpha();
    if (alpha == 0) return QColor(Qt::transparent);
    const auto channel = [&](int value) {
        const auto premultiplied = qRound(value * alpha / 255.);
        const auto filtered = feedbackChannel(premultiplied, alpha, level,
            tokens.brightnessDelta, tokens.contrastDelta);
        return qRound(filtered * 255. / alpha);
    };
    return QColor(channel(color.red()), channel(color.green()), channel(color.blue()), alpha);
}

qreal contrastRatio(QColor foreground, QColor background) {
    const auto a = luminance(foreground);
    const auto b = luminance(background);
    return (std::max(a, b) + .05) / (std::min(a, b) + .05);
}

QColor bestContrastAccent(QColor accent, QColor surface, qreal minimum) {
    const auto opaque = composite(accent, surface);
    return contrastRatio(opaque, surface) >= minimum ? opaque : bestText(surface);
}

QEasingCurve springCurve() {
    auto result = QEasingCurve(QEasingCurve::BezierSpline);
    result.addCubicBezierSegment(QPointF(.34, 1.56),
        QPointF(.64, 1.), QPointF(1., 1.));
    return result;
}

class FeedbackEffect final : public QGraphicsEffect {
public:
    FeedbackEffect(QWidget *owner, Tokens tokens)
    : QGraphicsEffect(owner), _tokens(tokens) {
        QObject::connect(&_animation, &QVariantAnimation::valueChanged,
            this, [this](const QVariant &value) {
                _level = value.toReal();
                update();
            });
    }

    void setTokens(Tokens tokens) {
        _tokens = tokens;
        updateBoundingRect();
        update();
    }

    void change(bool down) {
        const auto to = down ? 1. : 0.;
        if (_target == to) {
            return;
        }
        _target = to;
        _animation.stop();
        _animation.setStartValue(_level);
        _animation.setEndValue(to);
        _animation.setDuration(effectiveDuration(_tokens.springMs, _tokens.animationSpeed));
        _animation.setEasingCurve(springCurve());
        _animation.start();
    }

    void reset() {
        _animation.stop();
        _target = _level = 0.;
        update();
    }

protected:
    QRectF boundingRectFor(const QRectF &rect) const override {
        return rect.adjusted(
            -rect.width() * _tokens.springOverscan,
            -rect.height() * _tokens.springOverscan,
            rect.width() * _tokens.springOverscan,
            rect.height() * _tokens.springOverscan);
    }

    void draw(QPainter *painter) override {
        QPoint offset;
        const auto source = sourcePixmap(Qt::LogicalCoordinates,
            &offset, QGraphicsEffect::NoPad);
        const auto body = QRectF(offset, QSizeF(source.size()) / source.devicePixelRatio());
        const auto scale = 1. - (1. - _tokens.pressScale) * _level;
        painter->save();
        painter->setRenderHint(QPainter::SmoothPixmapTransform);
        painter->translate(body.center());
        painter->scale(scale, scale);
        painter->translate(-body.center());
        painter->drawPixmap(offset, source);
        painter->restore();
    }

private:
    Tokens _tokens;
    QVariantAnimation _animation;
    qreal _target = 0.;
    qreal _level = 0.;
};

QImage luminanceIconImage(QImage iconImage, qreal level, const Tokens &tokens) {
    if (level == 0.) {
        return iconImage;
    }
    auto result = iconImage.convertToFormat(QImage::Format_ARGB32_Premultiplied);
    for (int y = 0; y < result.height(); ++y) {
        auto pixels = reinterpret_cast<QRgb *>(result.scanLine(y));
        for (int x = 0; x < result.width(); ++x) {
            const auto pixel = pixels[x];
            const auto alpha = qAlpha(pixel);
            const auto channel = [&](int value) {
                return feedbackChannel(value, alpha, level,
                    tokens.brightnessDelta, tokens.contrastDelta);
            };
            pixels[x] = qRgba(channel(qRed(pixel)), channel(qGreen(pixel)),
                channel(qBlue(pixel)), alpha);
        }
    }
    return result;
}

class IconFeedback final : public QObject {
public:
    IconFeedback(QWidget *owner, Tokens tokens)
    : QObject(owner), _owner(owner), _tokens(tokens) {
        QObject::connect(&_animation, &QVariantAnimation::valueChanged,
            this, [this](const QVariant &value) {
                _level = value.toReal();
                _owner->update();
            });
    }
    void setTokens(Tokens tokens) { _tokens = tokens; _owner->update(); }
    qreal level() const { return _level; }
    void change(bool down) {
        const auto to = down ? 1. : 0.;
        if (_target == to) {
            return;
        }
        _target = to;
        _animation.stop();
        _animation.setStartValue(_level);
        _animation.setEndValue(to);
        _animation.setDuration(effectiveDuration(down ? _tokens.pressMs
            : _tokens.releaseMs, _tokens.animationSpeed));
        _animation.setEasingCurve(QEasingCurve::OutCubic);
        _animation.start();
    }
    void reset() {
        if (_target == 0. && _level == 0.
            && _animation.state() != QAbstractAnimation::Running) return;
        _animation.stop();
        _target = _level = 0.;
        _owner->update();
    }
    void paint(QPainter *painter, const QIcon &icon, QRect area,
        QIcon::Mode mode = QIcon::Normal, QIcon::State state = QIcon::Off) const {
        if (_level == 0.) {
            icon.paint(painter, area, Qt::AlignCenter, mode, state);
            return;
        }
        const auto pixels = icon.pixmap(area.size(),
            painter->device()->devicePixelRatioF(), mode, state);
        painter->drawImage(area, luminanceIconImage(pixels.toImage(), _level, _tokens));
    }
private:
    QWidget *_owner;
    Tokens _tokens;
    QVariantAnimation _animation;
    qreal _target = 0.;
    qreal _level = 0.;
};

class FeedbackBinder final : public QObject {
public:
    static FeedbackBinder *attach(QAbstractButton *button,
        Tokens tokens) {
        if (button->graphicsEffect()) {
            return nullptr;
        }
        return new FeedbackBinder(button, tokens);
    }
protected:
    bool eventFilter(QObject *object, QEvent *event) override {
        if (object != _button || !_effect) {
            return false;
        }
        const auto type = event->type();
        if (type == QEvent::Hide || type == QEvent::FocusOut
            || type == QEvent::WindowDeactivate
            || (type == QEvent::EnabledChange && !_button->isEnabled())) {
            _effect->reset();
        } else {
            synchronize();
        }
        return false;
    }
private:
    FeedbackBinder(QAbstractButton *button, Tokens tokens)
    : QObject(button), _button(button),
      _effect(new FeedbackEffect(button, tokens)) {
        button->setGraphicsEffect(_effect);
        button->installEventFilter(this);
        QObject::connect(button, &QAbstractButton::pressed,
            this, [this] { synchronize(); });
        QObject::connect(button, &QAbstractButton::released,
            this, [this] { synchronize(); });
    }
    void synchronize() {
        if (_effect) {
            _effect->change(_button->isEnabled() && _button->isDown());
        }
    }
    QAbstractButton *_button;
    QPointer<FeedbackEffect> _effect;
};

class Action final : public QAbstractButton {
public:
    Action(QWidget *parent, Tokens tokens, ButtonRole role,
        RowKind row = RowKind::None)
    : QAbstractButton(parent), _tokens(tokens), _role(role), _row(row) {
        setFocusPolicy(Qt::StrongFocus);
        setMouseTracking(true);
        setAutoRepeat(false);
        auto policy = QSizePolicy(QSizePolicy::Preferred, QSizePolicy::Preferred);
        policy.setHeightForWidth(row != RowKind::None);
        setSizePolicy(policy);
        if (row == RowKind::None) {
            _effect = new FeedbackEffect(this, tokens);
            setGraphicsEffect(_effect);
        } else {
            _icons = new IconFeedback(this, tokens);
        }
    }

    void setTokens(Tokens tokens) {
        _tokens = tokens;
        if (_effect) _effect->setTokens(tokens);
        if (_icons) _icons->setTokens(tokens);
        updateGeometry();
        update();
    }

    void setSecondary(QString text) {
        _secondary = std::move(text);
        updateGeometry();
        update();
    }
    void setTrailingIcon(QIcon icon) {
        _trailingIcon = std::move(icon);
        updateGeometry();
        synchronize();
    }

    void setRoundedAncestor(std::optional<RoundedAncestor> ancestor) {
        setCornerContext({ancestor ? CornerContext::Kind::Adjacent
            : CornerContext::Kind::None, Corners::All, ancestor, 0.});
    }

    void setCornerContext(CornerContext context) {
        Q_ASSERT_X(context.kind != CornerContext::Kind::Adjacent || context.ancestor,
            "Action::setCornerContext", "Adjacent corners require equal painted insets");
        _corners = context;
        update();
    }

    void setPaintInset(QMarginsF inset) {
        _paintInset = inset;
        update();
    }

    QSize sizeHint() const override {
        const auto width = fontMetrics().horizontalAdvance(text())
            + 2 * _tokens.textInset
            + (icon().isNull() ? 0 : _tokens.iconSize + _tokens.iconGap)
            + (_trailingIcon.isNull() ? 0 : _tokens.iconSize + _tokens.iconGap);
        return QSize(width, heightForWidth(width));
    }

    int heightForWidth(int width) const override {
        if (_row == RowKind::None) {
            return _tokens.buttonHeight;
        }
        const auto nominal = _row == RowKind::Menu
            ? _tokens.menuHeight : _row == RowKind::Navigation && _secondary.isEmpty()
            ? _tokens.navigationHeight : _tokens.rowHeight;
        return std::max(nominal, captionHeight(width)
            + 2 * _tokens.rowVerticalInset);
    }

protected:
    bool event(QEvent *event) override {
        const auto type = event->type();
        if (type == QEvent::KeyPress || type == QEvent::KeyRelease) {
            const auto key = static_cast<QKeyEvent *>(event);
            const auto enter = key->key() == Qt::Key_Return
                || key->key() == Qt::Key_Enter;
            const auto activation = enter || key->key() == Qt::Key_Space;
            if (activation && key->isAutoRepeat()) {
                return true;
            }
            if (enter && isEnabled()) {
                const auto activate = type == QEvent::KeyRelease && isDown();
                setDown(type == QEvent::KeyPress);
                synchronize();
                if (activate) {
                    click();
                }
                return true;
            }
        }
        if (type == QEvent::Enter) {
            _over = true;
        } else if (type == QEvent::Leave) {
            _over = false;
        } else if (type == QEvent::MouseMove) {
            _over = rect().contains(
                static_cast<QMouseEvent *>(event)->position().toPoint());
        }
        // 信号处理器可以销毁按钮，事件返回后先核对对象仍在。
        QPointer<Action> alive(this);
        const auto handled = QAbstractButton::event(event);
        if (!alive) {
            return handled;
        }
        const auto resetHover = type == QEvent::Hide
            || type == QEvent::WindowDeactivate
            || (type == QEvent::EnabledChange && !isEnabled());
        const auto cancel = resetHover || type == QEvent::FocusOut
            || type == QEvent::UngrabMouse;
        if (cancel) {
            setDown(false);
            if (resetHover) {
                _over = false;
            }
            if (_effect) _effect->reset();
            if (_icons) _icons->reset();
            update();
        } else if (type == QEvent::Enter || type == QEvent::Leave
            || type == QEvent::MouseMove || type == QEvent::MouseButtonPress
            || type == QEvent::MouseButtonRelease || type == QEvent::KeyPress
            || type == QEvent::KeyRelease) {
            synchronize();
        }
        return handled;
    }

    void paintEvent(QPaintEvent *) override {
        QPainter painter(this);
        painter.setRenderHint(QPainter::Antialiasing);
        auto body = QRectF(rect()).adjusted(_tokens.frameInset,
            _tokens.frameInset, -_tokens.frameInset, -_tokens.frameInset);
        const auto list = _row != RowKind::None;
        auto background = _tokens.surface;
        auto foreground = _tokens.foreground;
        bool fill = true;
        if (list) {
            if (_over && isEnabled()) {
                background = _row == RowKind::Card ? _tokens.cardHover
                    : _tokens.navigationHover;
            }
            if (_row == RowKind::Navigation || _row == RowKind::Menu) {
                body = QRectF(rect()).marginsRemoved(_row == RowKind::Menu
                    ? _tokens.menuHoverInset : _tokens.rowHoverInset);
                fill = _over && isEnabled();
            }
            if (_paintInset) {
                body = QRectF(rect()).marginsRemoved(*_paintInset);
            }
            background = feedbackColor(background, _icons->level(), _tokens);
        } else if (_role == ButtonRole::Plain) {
            fill = false;
        } else {
            background = _role == ButtonRole::Primary
                ? composite(_tokens.accent, _tokens.surface)
                : mix(_tokens.surface, bestText(_tokens.surface),
                    _tokens.neutralMix);
            if (_over && isEnabled()) {
                background = mix(background, bestText(background),
                    _tokens.hoverMix);
            }
            foreground = bestText(background);
        }
        const auto path = bodyPath(body);
        if (fill) {
            painter.setPen(Qt::NoPen);
            painter.setBrush(background);
            painter.drawPath(path);
        }
        auto label = rect().adjusted(_tokens.textInset, 0,
            -_tokens.textInset, 0);
        if (!_trailingIcon.isNull()) {
            const auto iconRect = QRect(label.right() - _tokens.iconSize + 1,
                (height() - _tokens.iconSize) / 2, _tokens.iconSize, _tokens.iconSize);
            paintIcon(&painter, _trailingIcon, iconRect);
            label.setRight(iconRect.left() - 1 - _tokens.iconGap);
        }
        if (!icon().isNull()) {
            const auto iconRect = QRect(label.left(),
                (height() - _tokens.iconSize) / 2,
                _tokens.iconSize, _tokens.iconSize);
            paintIcon(&painter, icon(), iconRect);
            label.setLeft(iconRect.right() + 1 + _tokens.iconGap);
        }
        painter.setPen(foreground);
        if (list && !_secondary.isEmpty()) {
            const auto total = captionHeight(width());
            const auto top = (height() - total) / 2;
            const auto title = QRect(label.left(), top,
                label.width(), fontMetrics().height());
            painter.drawText(title, Qt::AlignLeft | Qt::AlignVCenter,
                fontMetrics().elidedText(text(), Qt::ElideRight, label.width()));
            label.setTop(title.bottom() + 1 + _tokens.secondaryGap);
            label.setHeight(total - title.height() - _tokens.secondaryGap);
            painter.setPen(_tokens.secondary);
            painter.drawText(label, Qt::AlignLeft | Qt::AlignTop
                | Qt::TextWordWrap, _secondary);
        } else {
            painter.drawText(label, list
                ? Qt::AlignLeft | Qt::AlignVCenter : Qt::AlignCenter,
                fontMetrics().elidedText(text(), Qt::ElideRight, label.width()));
        }
        if (hasFocus()) {
            painter.setBrush(Qt::NoBrush);
            painter.setPen(QPen(foreground, _tokens.focusWidth));
            painter.drawPath(path);
        }
    }

private:
    void paintIcon(QPainter *painter, const QIcon &icon, QRect area) const {
        const auto mode = isEnabled() ? QIcon::Normal : QIcon::Disabled;
        const auto state = isChecked() ? QIcon::On : QIcon::Off;
        if (_icons) _icons->paint(painter, icon, area, mode, state);
        else icon.paint(painter, area, Qt::AlignCenter, mode, state);
    }
    QPainterPath bodyPath(QRectF body) const {
        const auto standalone = _row == RowKind::None ? _tokens.buttonRadius
            : _row == RowKind::Menu ? _tokens.menuItemRadius : _tokens.rowRadius;
        auto radii = std::array<qreal, 4>{standalone, standalone,
            standalone, standalone};
        if (_row != RowKind::None
            && _corners.kind == CornerContext::Kind::GroupCap) {
            radii.fill(_corners.groupRadius);
        } else if (_row != RowKind::None
            && _corners.kind == CornerContext::Kind::Adjacent) {
            for (int i = 0; i < 4; ++i) {
                if (static_cast<unsigned>(_corners.corners) & (1u << i)) {
                    radii[i] = derivedRadius(standalone, _corners.ancestor);
                }
            }
        }
        return roundedPath(body, radii);
    }
    int captionHeight(int width) const {
        const auto labelWidth = std::max(1, width - 2 * _tokens.textInset
            - (icon().isNull() ? 0 : _tokens.iconSize + _tokens.iconGap)
            - (_trailingIcon.isNull() ? 0 : _tokens.iconSize + _tokens.iconGap));
        const auto secondaryHeight = _secondary.isEmpty() ? 0
            : fontMetrics().boundingRect(QRect(0, 0, labelWidth, 0),
                Qt::TextWordWrap, _secondary).height();
        return fontMetrics().height() + (secondaryHeight == 0 ? 0
            : _tokens.secondaryGap + secondaryHeight);
    }
    void synchronize() {
        const auto down = isEnabled() && isDown();
        if (_effect) _effect->change(down);
        if (_icons) _icons->change(down);
        update();
    }
    Tokens _tokens;
    ButtonRole _role;
    RowKind _row;
    CornerContext _corners;
    std::optional<QMarginsF> _paintInset;
    QString _secondary;
    FeedbackEffect *_effect = nullptr;
    IconFeedback *_icons = nullptr;
    QIcon _trailingIcon;
    bool _over = false;
};

qreal surfaceRadius(const Tokens &tokens, SurfaceKind kind) {
    return kind == SurfaceKind::Dialog ? tokens.dialogRadius
        : kind == SurfaceKind::Menu ? tokens.menuRadius : tokens.dropdownRadius;
}

class RoundedClipEffect final : public QGraphicsEffect {
public:
    RoundedClipEffect(QWidget *owner, qreal radius)
    : QGraphicsEffect(owner), _owner(owner), _radius(radius) {
    }
    void setRadius(qreal radius) {
        _radius = radius;
        _cacheKey = 0;
        update();
    }
protected:
    void draw(QPainter *painter) override {
        QPoint offset;
        const auto source = sourcePixmap(Qt::LogicalCoordinates,
            &offset, QGraphicsEffect::NoPad);
        if (_cacheKey != source.cacheKey()) {
            _cacheKey = source.cacheKey();
            _cache = source.toImage().convertToFormat(
                QImage::Format_ARGB32_Premultiplied);
            auto mask = QImage(_cache.size(), QImage::Format_ARGB32_Premultiplied);
            mask.setDevicePixelRatio(_cache.devicePixelRatio());
            mask.fill(Qt::transparent);
            {
                QPainter paintMask(&mask);
                paintMask.setRenderHint(QPainter::Antialiasing);
                paintMask.setPen(Qt::NoPen);
                paintMask.setBrush(Qt::white);
                paintMask.drawRoundedRect(QRectF(_owner->rect()).translated(-offset),
                    _radius, _radius);
            }
            QPainter paintCache(&_cache);
            paintCache.setCompositionMode(QPainter::CompositionMode_DestinationIn);
            paintCache.drawImage(QPoint(), mask);
        }
        painter->drawImage(offset, _cache);
    }
private:
    QWidget *_owner;
    qreal _radius;
    qint64 _cacheKey = 0;
    QImage _cache;
};

class RoundedBody final : public QWidget {
public:
    RoundedBody(QWidget *parent, Tokens tokens, SurfaceKind kind)
    : QWidget(parent), _tokens(tokens), _kind(kind) {
        _clip = new RoundedClipEffect(this, surfaceRadius(tokens, kind));
        setGraphicsEffect(_clip);
    }
    qreal radius() const { return surfaceRadius(_tokens, _kind); }
    void setTokens(Tokens tokens) {
        _tokens = tokens;
        _clip->setRadius(radius());
        update();
    }
    bool contains(QPointF point) const {
        QPainterPath path;
        path.addRoundedRect(QRectF(rect()), radius(), radius());
        return path.contains(point);
    }
protected:
    void paintEvent(QPaintEvent *) override {
        QPainter painter(this);
        painter.setRenderHint(QPainter::Antialiasing);
        painter.setPen(Qt::NoPen);
        painter.setBrush(_tokens.surface);
        painter.drawRoundedRect(QRectF(rect()), radius(), radius());
    }
private:
    Tokens _tokens;
    SurfaceKind _kind;
    RoundedClipEffect *_clip = nullptr;
};

class RoundedSurface final : public QWidget {
public:
    RoundedSurface(QWidget *parent, Tokens tokens, SurfaceKind kind)
    : QWidget(parent) {
        setAutoFillBackground(false);
        _layout = new QVBoxLayout(this);
        _body = new RoundedBody(this, tokens, kind);
        _layout->addWidget(_body);
        _shadow = new QGraphicsDropShadowEffect(this);
        setGraphicsEffect(_shadow);
        setTokens(tokens);
    }
    RoundedBody *body() const { return _body; }
    void setTokens(Tokens tokens) {
        _body->setTokens(tokens);
        _shadow->setBlurRadius(tokens.shadowBlur);
        _shadow->setOffset(tokens.shadowOffset);
        auto color = QColor(Qt::black);
        color.setAlphaF(tokens.shadowOpacity);
        _shadow->setColor(color);
        const auto content = QRectF(0., 0., 100., 100.);
        const auto bounds = _shadow->boundingRectFor(content);
        _layout->setContentsMargins(qCeil(-bounds.left()), qCeil(-bounds.top()),
            qCeil(bounds.right() - content.right()),
            qCeil(bounds.bottom() - content.bottom()));
    }
private:
    RoundedBody *_body = nullptr;
    QVBoxLayout *_layout = nullptr;
    QGraphicsDropShadowEffect *_shadow = nullptr;
};

class MenuDismissLayer final : public QWidget {
public:
    MenuDismissLayer(QWidget *host, RoundedSurface *menu)
    : QWidget(host), _menu(menu) {
        setAttribute(Qt::WA_NoSystemBackground);
        setAttribute(Qt::WA_AcceptTouchEvents);
        setFocusPolicy(Qt::StrongFocus);
        menu->setParent(this);
        qApp->installEventFilter(this);
        hide();
    }
    bool showAt(QRect geometry) {
        if (!_menu || (_dismissed && sequenceActive())) {
            return false;
        }
        _dismissed = false;
        _buttons = Qt::NoButton;
        _touch = false;
        _escape = false;
        setGeometry(parentWidget()->rect());
        _menu->setGeometry(geometry);
        _menu->show();
        show();
        raise();
        setFocus(Qt::PopupFocusReason);
        return true;
    }
    void closeMenu() {
        _buttons |= QApplication::mouseButtons();
        dismiss();
        finish();
    }
protected:
    void mousePressEvent(QMouseEvent *event) override { event->accept(); }
    void mouseReleaseEvent(QMouseEvent *event) override { event->accept(); }
    void mouseMoveEvent(QMouseEvent *event) override { event->accept(); }
    void wheelEvent(QWheelEvent *event) override { event->accept(); }
    bool eventFilter(QObject *object, QEvent *event) override {
        if (object == parentWidget() && event->type() == QEvent::Resize) {
            setGeometry(parentWidget()->rect());
            return false;
        }
        const auto type = event->type();
        if ((object == parentWidget() || object == this)
            && (type == QEvent::WindowDeactivate || type == QEvent::Hide
                || (object == this && type == QEvent::UngrabMouse))) {
            cancelSequence();
            return false;
        }
        const auto target = qobject_cast<QWidget *>(object);
        if (!isVisible() || !target
            || (target != this && !isAncestorOf(target))) {
            return false;
        }
        if (type == QEvent::MouseButtonPress
            || type == QEvent::MouseButtonDblClick) {
            const auto mouse = static_cast<QMouseEvent *>(event);
            const auto point = mapFromGlobal(mouse->globalPosition().toPoint());
            if (!_dismissed && inside(point)) {
                return false;
            }
            _buttons = mouse->buttons() | mouse->button();
            dismiss();
            grabMouse();
        } else if (_dismissed && type == QEvent::MouseButtonRelease) {
            _buttons = static_cast<QMouseEvent *>(event)->buttons();
            finish();
        } else if (_dismissed && (type == QEvent::MouseMove
            || type == QEvent::Wheel)) {
        } else if (type == QEvent::TouchBegin) {
            const auto touch = static_cast<QTouchEvent *>(event);
            if (touch->points().isEmpty()) {
                return false;
            }
            const auto point = mapFromGlobal(
                touch->points().first().globalPosition().toPoint());
            if (!_dismissed && inside(point)) {
                return false;
            }
            _touch = true;
            dismiss();
        } else if (_touch && type == QEvent::TouchUpdate) {
        } else if (type == QEvent::TouchCancel) {
            cancelSequence();
        } else if (_touch && type == QEvent::TouchEnd) {
            _touch = false;
            finish();
        } else if (type == QEvent::KeyPress
            && static_cast<QKeyEvent *>(event)->key() == Qt::Key_Escape) {
            _escape = true;
            dismiss();
            setFocus(Qt::PopupFocusReason);
        } else if (_escape && type == QEvent::KeyRelease
            && static_cast<QKeyEvent *>(event)->key() == Qt::Key_Escape) {
            _escape = false;
            finish();
        } else {
            return false;
        }
        event->accept();
        return true;
    }
private:
    bool inside(QPoint point) const {
        return _menu && _menu->body()->contains(
            _menu->body()->mapFrom(this, point));
    }
    void dismiss() {
        _dismissed = true;
        if (_menu) {
            _menu->hide();
        }
    }
    void finish() {
        if (sequenceActive()) {
            return;
        }
        if (QWidget::mouseGrabber() == this) {
            releaseMouse();
        }
        hide();
    }
    void cancelSequence() {
        if (_cancelling) {
            return;
        }
        _cancelling = true;
        _buttons = Qt::NoButton;
        _touch = _escape = false;
        dismiss();
        finish();
        _cancelling = false;
    }
    bool sequenceActive() const {
        return _buttons != Qt::NoButton || _touch || _escape;
    }
    QPointer<RoundedSurface> _menu;
    Qt::MouseButtons _buttons = Qt::NoButton;
    bool _touch = false;
    bool _escape = false;
    bool _cancelling = false;
    bool _dismissed = false;
};

class FadeOverlay final : public QWidget {
public:
    explicit FadeOverlay(QWidget *parent) : QWidget(parent) {
        setFocusPolicy(Qt::StrongFocus);
        hide();
    }
    QPixmap from;
    QPixmap to;
    qreal progress = 0.;
protected:
    void paintEvent(QPaintEvent *) override {
        QPainter painter(this);
        painter.drawPixmap(rect(), from);
        painter.setOpacity(progress);
        painter.drawPixmap(rect(), to);
    }
    bool focusNextPrevChild(bool) override { return true; }
    bool event(QEvent *event) override {
        if (event->type() == QEvent::KeyPress
            || event->type() == QEvent::KeyRelease
            || event->type() == QEvent::MouseButtonPress
            || event->type() == QEvent::MouseButtonRelease
            || event->type() == QEvent::Wheel) {
            event->accept();
            return true;
        }
        return QWidget::event(event);
    }
};

class CrossfadeStack final : public QWidget {
public:
    CrossfadeStack(QWidget *parent, Tokens tokens)
    : QWidget(parent), _tokens(tokens) {
        _stack = new QStackedWidget(this);
        _overlay = new FadeOverlay(this);
        auto layout = new QVBoxLayout(this);
        layout->setContentsMargins(0, 0, 0, 0);
        layout->addWidget(_stack);
        QObject::connect(&_animation, &QVariantAnimation::valueChanged,
            this, [this](const QVariant &value) {
                _overlay->progress = value.toReal();
                _overlay->update();
            });
        QObject::connect(&_animation, &QVariantAnimation::finished,
            this, [this] { finish(); });
    }
    int addWidget(QWidget *page) { return _stack->addWidget(page); }
    void setTokens(Tokens tokens) { _tokens = tokens; }
    bool setIndex(int index) {
        if (index < 0 || index >= _stack->count()) {
            return false;
        }
        if (_stack->currentIndex() == index) {
            return true;
        }
        const auto running = _animation.state() == QAbstractAnimation::Running;
        const auto from = snapshot(running ? static_cast<QWidget *>(_overlay)
            : static_cast<QWidget *>(_stack));
        if (!running) {
            _wasEnabled = _stack->isEnabled();
            const auto focused = QApplication::focusWidget();
            _capturedFocus = focused
                && (focused == _stack || _stack->isAncestorOf(focused));
        }
        _animation.stop();
        _stack->setEnabled(_wasEnabled);
        _stack->setCurrentIndex(index);
        _stack->layout()->activate();
        const auto to = snapshot(_stack);
        _stack->setEnabled(false);
        _overlay->from = from;
        _overlay->to = to;
        _overlay->progress = 0.;
        _overlay->setGeometry(rect());
        _overlay->show();
        _overlay->raise();
        if (_capturedFocus) {
            _overlay->setFocus(Qt::OtherFocusReason);
        }
        _animation.setStartValue(0.);
        _animation.setEndValue(1.);
        _animation.setDuration(effectiveDuration(
            _tokens.tabsMs, _tokens.animationSpeed));
        _animation.setEasingCurve(QEasingCurve::Linear);
        _animation.start();
        return true;
    }
protected:
    void resizeEvent(QResizeEvent *) override { _overlay->setGeometry(rect()); }
    void hideEvent(QHideEvent *) override {
        if (_animation.state() == QAbstractAnimation::Running) {
            _animation.stop();
            finish();
        }
    }
private:
    QPixmap snapshot(QWidget *widget) const {
        auto result = QPixmap(widget->size() * devicePixelRatioF());
        result.setDevicePixelRatio(devicePixelRatioF());
        result.fill(_tokens.surface);
        QPainter painter(&result);
        painter.drawPixmap(0, 0, widget->grab());
        return result;
    }
    void finish() {
        _overlay->hide();
        _stack->setEnabled(_wasEnabled);
        if (_capturedFocus) {
            _stack->setFocus(Qt::OtherFocusReason);
        }
    }
    Tokens _tokens;
    QStackedWidget *_stack = nullptr;
    FadeOverlay *_overlay = nullptr;
    QVariantAnimation _animation;
    bool _wasEnabled = true;
    bool _capturedFocus = false;
};

class CrossfadeTabs final : public QTabBar {
public:
    CrossfadeTabs(QWidget *parent, Tokens tokens)
    : QTabBar(parent), _tokens(tokens) {
        setDrawBase(false);
        setExpanding(true);
        setMouseTracking(true);
        setFocusPolicy(Qt::StrongFocus);
        QObject::connect(this, &QTabBar::currentChanged,
            this, [this](int index) {
                _animation.stop();
                _weights.resize(count());
                if (_selected < 0 && index >= 0) {
                    _weights.fill(0.);
                    _weights[index] = 1.;
                    _selected = index;
                    update();
                    return;
                }
                _from = _weights;
                _selected = index;
                _animation.setStartValue(0.);
                _animation.setEndValue(1.);
                _animation.setDuration(effectiveDuration(
                    _tokens.tabsMs, _tokens.animationSpeed));
                _animation.setEasingCurve(QEasingCurve::Linear);
                _animation.start();
            });
        QObject::connect(&_animation, &QVariantAnimation::valueChanged,
            this, [this](const QVariant &value) {
                const auto progress = value.toReal();
                for (int i = 0; i < _weights.size(); ++i) {
                    const auto target = i == _selected ? 1. : 0.;
                    _weights[i] = _from[i] + (target - _from[i]) * progress;
                }
                update();
            });
    }
    void setTokens(Tokens tokens) {
        _tokens = tokens;
        for (const auto icon : _iconFeedback) icon->setTokens(tokens);
        update();
    }
protected:
    void tabInserted(int index) override {
        QTabBar::tabInserted(index);
        _iconFeedback.insert(index, new IconFeedback(this, _tokens));
        synchronizeIcons();
    }
    void tabRemoved(int index) override {
        QTabBar::tabRemoved(index);
        delete _iconFeedback.takeAt(index);
        _hovered = _mouseOrigin = _keyboardPressed = -1;
        synchronizeIcons();
    }
    bool event(QEvent *event) override {
        const auto type = event->type();
        if (type == QEvent::Paint) synchronizeIcons();
        if (type == QEvent::Enter) {
            _hovered = tabAt(static_cast<QEnterEvent *>(event)->position().toPoint());
        } else if (type == QEvent::Leave) {
            _hovered = -1;
        } else if (type == QEvent::MouseMove) {
            _hovered = tabAt(static_cast<QMouseEvent *>(event)->position().toPoint());
        } else if (type == QEvent::MouseButtonPress
            || type == QEvent::MouseButtonDblClick) {
            const auto mouse = static_cast<QMouseEvent *>(event);
            _hovered = tabAt(mouse->position().toPoint());
            if (mouse->button() == Qt::LeftButton) {
                _mouseOrigin = isEnabled() && _hovered >= 0
                    && isTabEnabled(_hovered) ? _hovered : -1;
                setFocus(Qt::MouseFocusReason);
                event->accept();
                synchronizeIcons();
                update();
                return true;
            }
        } else if (type == QEvent::MouseButtonRelease) {
            const auto mouse = static_cast<QMouseEvent *>(event);
            _hovered = tabAt(mouse->position().toPoint());
            if (mouse->button() == Qt::LeftButton) {
                const auto activate = isEnabled() && _mouseOrigin >= 0
                    && _mouseOrigin == _hovered && isTabEnabled(_mouseOrigin);
                const auto origin = _mouseOrigin;
                _mouseOrigin = -1;
                QPointer<CrossfadeTabs> alive(this);
                if (activate) {
                    setCurrentIndex(origin);
                }
                if (!alive) {
                    return true;
                }
                event->accept();
                synchronizeIcons();
                update();
                return true;
            }
        } else if (type == QEvent::KeyPress || type == QEvent::KeyRelease) {
            const auto key = static_cast<QKeyEvent *>(event);
            if (key->key() == Qt::Key_Space || key->key() == Qt::Key_Return
                || key->key() == Qt::Key_Enter) {
                if (!key->isAutoRepeat()) {
                    _keyboardPressed = type == QEvent::KeyPress
                        ? currentIndex() : -1;
                }
                event->accept();
                synchronizeIcons();
                update();
                return true;
            }
        }
        QPointer<CrossfadeTabs> alive(this);
        const auto result = QTabBar::event(event);
        if (!alive) {
            return result;
        }
        const auto cancelled = type == QEvent::Hide || type == QEvent::WindowDeactivate
            || type == QEvent::FocusOut || type == QEvent::UngrabMouse
            || (type == QEvent::EnabledChange && !isEnabled());
        if (cancelled) {
            _hovered = _mouseOrigin = _keyboardPressed = -1;
            for (const auto icon : _iconFeedback) icon->reset();
        }
        if (cancelled || type == QEvent::Enter || type == QEvent::Leave
            || type == QEvent::MouseMove || type == QEvent::MouseButtonPress
            || type == QEvent::MouseButtonRelease || type == QEvent::KeyPress
            || type == QEvent::KeyRelease) {
            synchronizeIcons();
            update();
        }
        return result;
    }
    void paintEvent(QPaintEvent *) override {
        QPainter painter(this);
        painter.setRenderHint(QPainter::Antialiasing);
        painter.fillRect(rect(), _tokens.surface);
        for (int i = 0; i < count(); ++i) {
            const auto weight = i < _weights.size() ? _weights[i] : 0.;
            const auto area = tabRect(i);
            auto background = _tokens.surface;
            const auto iconOnly = !tabIcon(i).isNull() && tabText(i).isEmpty();
            const auto enabled = isEnabled() && isTabEnabled(i);
            const auto pressed = enabled && (_keyboardPressed == i
                || (_mouseOrigin == i && _hovered == i));
            const auto hover = enabled && _hovered == i && i != currentIndex();
            if (!iconOnly && (hover || pressed)) {
                background = feedbackColor(_tokens.textTabHover, _iconFeedback[i]->level(), _tokens);
                painter.setPen(Qt::NoPen);
                painter.setBrush(background);
                painter.drawRoundedRect(area.adjusted(_tokens.tabsInset,
                    _tokens.tabsInset, -_tokens.tabsInset, -_tokens.tabsInset),
                    _tokens.tabsRadius, _tokens.tabsRadius);
            }
            const auto selected = bestContrastAccent(_tokens.accent,
                background, _tokens.selectedTextContrast);
            auto textColor = weight > 0. ? mix(
                composite(_tokens.foreground, background), selected, weight)
                : _tokens.foreground;
            if (weight > 0.) {
                textColor = bestContrastAccent(textColor, background,
                    _tokens.selectedTextContrast);
            }
            painter.setPen(textColor);
            if (iconOnly) {
                const auto side = _tokens.iconSize;
                const auto iconRect = QRect(area.center().x() - side / 2,
                    area.center().y() - side / 2, side, side);
                tabIcon(i).paint(&painter, iconRect, Qt::AlignCenter,
                    enabled ? QIcon::Normal : QIcon::Disabled,
                    i == currentIndex() ? QIcon::On : QIcon::Off);
            } else if (tabIcon(i).isNull()) {
                painter.drawText(area, Qt::AlignCenter, tabText(i));
            } else {
                const auto available = std::max(1, area.width() - 2 * _tokens.tabsInset
                    - _tokens.iconSize - _tokens.iconGap);
                const auto caption = fontMetrics().elidedText(tabText(i), Qt::ElideRight, available);
                const auto total = _tokens.iconSize + _tokens.iconGap
                    + fontMetrics().horizontalAdvance(caption);
                const auto left = area.center().x() - total / 2;
                const auto iconRect = QRect(left, area.center().y() - _tokens.iconSize / 2,
                    _tokens.iconSize, _tokens.iconSize);
                _iconFeedback[i]->paint(&painter, tabIcon(i), iconRect,
                    enabled ? QIcon::Normal : QIcon::Disabled,
                    i == currentIndex() ? QIcon::On : QIcon::Off);
                painter.drawText(QRect(iconRect.right() + 1 + _tokens.iconGap,
                    area.top(), total - _tokens.iconSize - _tokens.iconGap, area.height()),
                    Qt::AlignLeft | Qt::AlignVCenter, caption);
            }
            // 指示线位于内缩背景之外，基色按真实 surface 核对。
            auto indicator = bestContrastAccent(_tokens.accent,
                _tokens.surface, _tokens.indicatorContrast);
            indicator.setAlphaF(weight);
            painter.setPen(Qt::NoPen);
            painter.setBrush(indicator);
            painter.drawRoundedRect(QRectF(area.left(), area.bottom()
                - _tokens.tabsIndicatorHeight + 1, area.width(),
                _tokens.tabsIndicatorHeight),
                _tokens.tabsIndicatorHeight / 2., _tokens.tabsIndicatorHeight / 2.);
            if (hasFocus() && currentIndex() == i) {
                painter.setPen(QPen(_tokens.foreground, _tokens.focusWidth));
                painter.setBrush(Qt::NoBrush);
                const auto inset = _tokens.frameInset;
                painter.drawRect(area.adjusted(inset, inset, -inset, -inset));
            }
        }
    }
private:
    void synchronizeIcons() {
        for (int i = 0; i < _iconFeedback.size(); ++i) {
            if (!isEnabled() || !isTabEnabled(i)) {
                if (_mouseOrigin == i) _mouseOrigin = -1;
                if (_keyboardPressed == i) _keyboardPressed = -1;
                _iconFeedback[i]->reset();
                continue;
            }
            const auto textual = tabIcon(i).isNull() || !tabText(i).isEmpty();
            const auto pressed = isEnabled() && isTabEnabled(i) && (_keyboardPressed == i
                || (_mouseOrigin == i && _hovered == i));
            _iconFeedback[i]->change(textual && pressed);
        }
    }
    Tokens _tokens;
    QVariantAnimation _animation;
    QList<qreal> _weights;
    QList<qreal> _from;
    QList<IconFeedback *> _iconFeedback;
    int _selected = -1;
    int _hovered = -1;
    int _mouseOrigin = -1;
    int _keyboardPressed = -1;
};

void bindTabs(CrossfadeTabs *tabs, CrossfadeStack *stack) {
    QObject::connect(tabs, &QTabBar::currentChanged, stack,
        [stack](int index) { stack->setIndex(index); });
}

QWidget *makeActions(QWidget *parent, Tokens tokens) {
    auto container = new RoundedSurface(parent, tokens, SurfaceKind::Dialog);
    auto body = container->body();
    auto layout = new QVBoxLayout(body);
    layout->setContentsMargins(tokens.surfaceInset, tokens.surfaceInset,
        tokens.surfaceInset, tokens.surfaceInset);
    auto plain = new Action(body, tokens, ButtonRole::Plain);
    plain->setText(QStringLiteral("查看日志"));
    auto primary = new Action(body, tokens, ButtonRole::Primary);
    primary->setText(QStringLiteral("连接"));
    auto dismiss = new Action(body, tokens, ButtonRole::Dismiss);
    dismiss->setText(QStringLiteral("取消"));
    layout->addWidget(plain);
    layout->addWidget(primary);
    layout->addWidget(dismiss);
    auto group = new RoundedBody(body, tokens, SurfaceKind::Dialog);
    auto rows = new QVBoxLayout(group);
    const auto padding = 7;
    rows->setContentsMargins(padding, padding, padding, padding);
    const auto ancestor = equalInsetAncestor(group->radius(),
        padding + tokens.frameInset, padding + tokens.frameInset);
    auto first = new Action(group, tokens, ButtonRole::Plain, RowKind::Card);
    first->setText(QStringLiteral("设备设置"));
    first->setSecondary(QStringLiteral("查看并管理连接选项"));
    first->setPaintInset(QMarginsF(tokens.frameInset, tokens.frameInset,
        tokens.frameInset, tokens.frameInset));
    first->setCornerContext({CornerContext::Kind::Adjacent,
        Corners::Top, ancestor, 0.});
    auto middle = new Action(group, tokens, ButtonRole::Plain, RowKind::Card);
    middle->setText(QStringLiteral("投屏参数"));
    middle->setCornerContext({CornerContext::Kind::GroupCap,
        Corners::All, std::nullopt, derivedRadius(tokens.rowRadius, ancestor)});
    auto last = new Action(group, tokens, ButtonRole::Plain, RowKind::Card);
    last->setText(QStringLiteral("会话设置"));
    last->setPaintInset(QMarginsF(tokens.frameInset, tokens.frameInset,
        tokens.frameInset, tokens.frameInset));
    last->setCornerContext({CornerContext::Kind::Adjacent,
        Corners::Bottom, ancestor, 0.});
    rows->addWidget(first);
    rows->addWidget(middle);
    rows->addWidget(last);
    layout->addWidget(group);
    return container;
}

}
```

API：[QAbstractButton](https://doc.qt.io/qt-6/qabstractbutton.html)、[QGraphicsEffect](https://doc.qt.io/qt-6/qgraphicseffect.html)、[QGraphicsDropShadowEffect](https://doc.qt.io/qt-6/qgraphicsdropshadoweffect.html)、[QVariantAnimation](https://doc.qt.io/qt-6/qvariantanimation.html)。

反馈通道来源：[click_feedback.cpp](https://github.com/Kindness-Kismet/AstraGram-Desktop/blob/2955a62bb2828c6ab82403c1ee5d40d3a9a75430/Telegram/SourceFiles/extras/ui/controls/click_feedback.cpp#L160)。
