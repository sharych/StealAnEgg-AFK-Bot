import Cocoa
import Carbon
import ScreenCaptureKit
import ApplicationServices

// MARK: - Top-Down Flipped Container View
final class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

// MARK: - Snapshot Settings Struct
struct BotSettings {
    var timeoutSec: Double
    var maxClicks: Int
    var clickSpeedMs: Int
    var postCatchWaitSec: Double
    var castX: Double
    var castY: Double
    var castHoldSec: Double
    var enable2xGlitch: Bool
    var rodKey: String
    var swapKey: String
    var randomize: Bool
    var enableAntiAfk: Bool
    var camMinSec: Int
    var camMaxSec: Int
    var camDragPx: Int
    var camArrowAssist: Bool

    static let defaults = BotSettings(
        timeoutSec: 9.0,
        maxClicks: 48,
        clickSpeedMs: 30,
        postCatchWaitSec: 2.5,
        castX: 1138.0,
        castY: 362.0,
        castHoldSec: 1.10,
        enable2xGlitch: true,
        rodKey: "9",
        swapKey: "3",
        randomize: true,
        enableAntiAfk: true,
        camMinSec: 45,
        camMaxSec: 110,
        camDragPx: 120,
        camArrowAssist: true
    )
}

// MARK: - ScreenCaptureKit STOP Button Radar (macOS 14+)
@available(macOS 14.0, *)
final class StopButtonRadar {
    private let displayLock = NSLock()
    private var cachedDisplay: SCDisplay?

    private func getCachedDisplay() -> SCDisplay? {
        displayLock.lock()
        defer { displayLock.unlock() }
        return cachedDisplay
    }

    private func setCachedDisplay(_ display: SCDisplay?) {
        displayLock.lock()
        cachedDisplay = display
        displayLock.unlock()
    }

    func scan(outRedCount: inout Int, outThreshold: inout Int) -> Bool {
        outRedCount = 0
        outThreshold = 40

        let semaphore = DispatchSemaphore(value: 0)
        var detected = false
        var count = 0
        var threshold = 40

        Task.detached { [weak self] in
            defer { semaphore.signal() }
            guard let self = self else { return }
            do {
                let display: SCDisplay
                if let cached = self.getCachedDisplay() {
                    display = cached
                } else {
                    let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
                    let mainID = CGMainDisplayID()
                    guard let found = content.displays.first(where: { $0.displayID == mainID }) ?? content.displays.first else {
                        return
                    }
                    self.setCachedDisplay(found)
                    display = found
                }

                let w = Double(display.width)
                let h = Double(display.height)
                let minX = w * 0.65
                let maxX = w * 0.85
                let minY = h * 0.80
                let maxY = h * 0.95

                let scanW = max(1, Int(maxX - minX))
                let scanH = max(1, Int(maxY - minY))
                let sampledPixels = max(1, ((scanW + 2) / 3) * ((scanH + 2) / 3))
                threshold = min(50, max(18, Int((Double(sampledPixels) * (40.0 / 6912.0)).rounded())))

                let filter = SCContentFilter(display: display, excludingApplications: [], exceptingWindows: [])
                let config = SCStreamConfiguration()
                config.sourceRect = CGRect(x: minX, y: minY, width: Double(scanW), height: Double(scanH))
                config.width = scanW
                config.height = scanH
                config.showsCursor = false
                config.pixelFormat = kCVPixelFormatType_32BGRA

                let cgImage = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
                count = Self.countRedStopPixels(in: cgImage)
                detected = (count >= threshold)
            } catch {
                self.setCachedDisplay(nil)
                detected = false
            }
        }

        _ = semaphore.wait(timeout: .now() + 1.5)
        outRedCount = count
        outThreshold = threshold
        return detected
    }

    func scan(outRedCount: inout Int) -> Bool {
        var threshold = 40
        return scan(outRedCount: &outRedCount, outThreshold: &threshold)
    }

    private static func countRedStopPixels(in image: CGImage) -> Int {
        guard let provider = image.dataProvider,
              let cfData = provider.data,
              let ptr = CFDataGetBytePtr(cfData) else {
            return 0
        }

        let width = image.width
        let height = image.height
        let bytesPerRow = image.bytesPerRow
        let bytesPerPixel = max(4, image.bitsPerPixel / 8)
        let dataLen = CFDataGetLength(cfData)

        var redCount = 0
        var y = 0
        while y < height {
            let rowOffset = y * bytesPerRow
            var x = 0
            while x < width {
                let idx = rowOffset + x * bytesPerPixel
                if idx + 2 < dataLen {
                    // kCVPixelFormatType_32BGRA byte order: B, G, R, A
                    let b = Int(ptr[idx])
                    let g = Int(ptr[idx + 1])
                    let r = Int(ptr[idx + 2])
                    if r > 220 && g > 85 && g < 145 && b > 75 && b < 140 {
                        redCount += 1
                    }
                }
                x += 3
            }
            y += 3
        }
        return redCount
    }
}

// MARK: - Global Carbon Hotkey Callback Bridge
private weak var sharedAppController: BotAppController?

private func carbonHotKeyHandler(
    nextHandler: EventHandlerCallRef?,
    event: EventRef?,
    userData: UnsafeMutableRawPointer?
) -> OSStatus {
    guard let event = event else { return noErr }
    var hkID = EventHotKeyID()
    let status = GetEventParameter(
        event,
        EventParamName(kEventParamDirectObject),
        EventParamType(typeEventHotKeyID),
        nil,
        MemoryLayout<EventHotKeyID>.size,
        nil,
        &hkID
    )
    if status == noErr {
        DispatchQueue.main.async {
            switch hkID.id {
            case 1: sharedAppController?.toggleFishingBot()
            case 2: sharedAppController?.doTurboClickTest()
            case 3: sharedAppController?.toggleTreadmillBot()
            default: break
            }
        }
    }
    return noErr
}

// MARK: - Main Application Controller
@available(macOS 14.0, *)
final class BotAppController: NSObject, NSApplicationDelegate, NSWindowDelegate, NSTextFieldDelegate {
    private enum BotMode {
        case idle
        case fishing
        case treadmill
    }

    private var window: NSWindow!
    private let radar = StopButtonRadar()

    // Header UI
    private var lblStatus: NSTextField!
    private var btnToggleFishing: NSButton!
    private var btnToggleTreadmill: NSButton!
    private var lblFishCount: NSTextField!
    private var lblElapsedTime: NSTextField!
    private var lblRadar: NSTextField!
    private var txtLog: NSTextView!

    // Tab 1 Controls
    private var txtTimeout: NSTextField!
    private var txtMaxClicks: NSTextField!
    private var txtClickSpeedMs: NSTextField!
    private var txtPostCatchWait: NSTextField!

    // Tab 2 Controls
    private var txtCastX: NSTextField!
    private var txtCastY: NSTextField!
    private var txtCastHold: NSTextField!
    private var chk2xGlitch: NSButton!
    private var txtRodKey: NSTextField!
    private var txtSwapKey: NSTextField!

    // Tab 3 Controls
    private var chkRandomize: NSButton!
    private var chkAntiAfk: NSButton!
    private var txtCamMinSec: NSTextField!
    private var txtCamMaxSec: NSTextField!
    private var txtCamDragPx: NSTextField!
    private var chkCamArrowAssist: NSButton!

    // State
    private let stateLock = NSLock()
    private var _isRunning = false
    private var _runGeneration: UInt64 = 0
    private var _activeMode: BotMode = .idle
    private var _isTestClicking = false

    private var isRunning: Bool {
        get { stateLock.lock(); defer { stateLock.unlock() }; return _isRunning }
    }

    private var activeMode: BotMode {
        get { stateLock.lock(); defer { stateLock.unlock() }; return _activeMode }
    }

    private let settingsLock = NSLock()
    private var cachedSettings = BotSettings.defaults

    private let radarQueue = DispatchQueue(label: "com.stealanegg.radar", qos: .userInteractive)
    private let radarBusyLock = NSLock()
    private var isRadarTimerBusy = false
    private var lastRadarUiUpdate = Date.distantPast

    private let logDateFormatter: DateFormatter = {
        let df = DateFormatter()
        df.dateFormat = "HH:mm:ss"
        return df
    }()

    private var startTime = Date()
    private var fishCaught = 0
    private var camRotationsDone = 0
    private var workerThread: Thread?
    private var uiTimer: Timer?
    private var radarTimer: Timer?
    private var hotKeyRefs: [EventHotKeyRef?] = []

    // Palette
    private let bgMain = NSColor(calibratedRed: 0.118, green: 0.118, blue: 0.180, alpha: 1.0)
    private let bgHeader = NSColor(calibratedRed: 0.094, green: 0.094, blue: 0.145, alpha: 1.0)
    private let bgInput = NSColor(calibratedRed: 0.192, green: 0.196, blue: 0.267, alpha: 1.0)
    private let fgText = NSColor(calibratedRed: 0.804, green: 0.839, blue: 0.957, alpha: 1.0)
    private let colGreen = NSColor(calibratedRed: 0.651, green: 0.890, blue: 0.631, alpha: 1.0)
    private let colBlue = NSColor(calibratedRed: 0.537, green: 0.706, blue: 0.980, alpha: 1.0)
    private let colYellow = NSColor(calibratedRed: 0.976, green: 0.886, blue: 0.686, alpha: 1.0)
    private let colRed = NSColor(calibratedRed: 0.953, green: 0.545, blue: 0.659, alpha: 1.0)
    private let colTeal = NSColor(calibratedRed: 0.580, green: 0.886, blue: 0.835, alpha: 1.0)

    func applicationDidFinishLaunching(_ notification: Notification) {
        sharedAppController = self
        setupMenu()
        buildUI()
        loadSettingsFromDefaults()
        syncSettingsFromUI(save: false)
        registerGlobalHotkeys()
        startTimers()
        checkPermissionsOnLaunch()
        log("Bot v5.1 (macOS) ready! Press F6 for Fishing Bot or F8 for Treadmill Camera AFK.")
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }

    func windowWillClose(_ notification: Notification) {
        syncSettingsFromUI(save: true)
        stateLock.lock()
        _isRunning = false
        _activeMode = .idle
        _runGeneration &+= 1
        stateLock.unlock()

        uiTimer?.invalidate()
        radarTimer?.invalidate()
        for ref in hotKeyRefs {
            if let r = ref { UnregisterEventHotKey(r) }
        }
    }

    private func setupMenu() {
        let mainMenu = NSMenu()
        let appMenuItem = NSMenuItem()
        mainMenu.addItem(appMenuItem)
        let appMenu = NSMenu()
        appMenu.addItem(NSMenuItem(title: "Quit StealAnEgg AFK Bot", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        appMenuItem.submenu = appMenu
        NSApp.mainMenu = mainMenu
    }

    private func checkPermissionsOnLaunch() {
        if !AXIsProcessTrusted() {
            log("⚠️ Accessibility permission not yet granted! Go to System Settings -> Privacy & Security -> Accessibility and enable StealAnEgg_AFK_Bot so clicks/keys can reach Roblox.")
        }
        if !CGPreflightScreenCaptureAccess() {
            log("⚠️ Screen Recording permission not yet granted! Requesting access or enable in System Settings -> Privacy & Security -> Screen Recording.")
            CGRequestScreenCaptureAccess()
        }
    }

    // MARK: - Build UI
    private func buildUI() {
        let winWidth: CGFloat = 680
        let winHeight: CGFloat = 760
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: winWidth, height: winHeight),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Steal an Egg - Smart AFK Fishing & Treadmill Bot (macOS v5.1)"
        window.center()
        window.delegate = self

        let root = FlippedView(frame: NSRect(x: 0, y: 0, width: winWidth, height: winHeight))
        root.wantsLayer = true
        root.layer?.backgroundColor = bgMain.cgColor
        window.contentView = root

        // Header Panel
        let header = FlippedView(frame: NSRect(x: 0, y: 0, width: winWidth, height: 135))
        header.wantsLayer = true
        header.layer?.backgroundColor = bgHeader.cgColor
        root.addSubview(header)

        lblStatus = makeLabel("STATUS: IDLE (F6 = Fishing Bot | F8 = Treadmill Speed AFK)", x: 20, y: 12, w: 640, size: 13, bold: true, color: colYellow)
        header.addSubview(lblStatus)

        btnToggleFishing = makeButton("START FISHING (F6)", x: 20, y: 44, w: 195, h: 44, bg: colGreen, fg: .black, action: #selector(onToggleFishingClick))
        header.addSubview(btnToggleFishing)

        btnToggleTreadmill = makeButton("TREADMILL AFK (F8)", x: 225, y: 44, w: 200, h: 44, bg: colBlue, fg: .black, action: #selector(onToggleTreadmillClick))
        header.addSubview(btnToggleTreadmill)

        lblFishCount = makeLabel("Fish Caught: 0", x: 445, y: 46, w: 210, size: 13, bold: true, color: colTeal)
        header.addSubview(lblFishCount)

        lblElapsedTime = makeLabel("Elapsed Time: 00:00:00", x: 445, y: 72, w: 210, size: 11.5, bold: false, color: fgText)
        header.addSubview(lblElapsedTime)

        lblRadar = makeLabel("Radar Status: Ready (Waiting for STOP button)", x: 20, y: 102, w: 640, size: 11.5, bold: true, color: colTeal)
        header.addSubview(lblRadar)

        // Tab View
        let tabView = NSTabView(frame: NSRect(x: 15, y: 145, width: 650, height: 365))
        root.addSubview(tabView)

        let tab1 = NSTabViewItem(identifier: "tab1")
        tab1.label = "1. Bite Detection & Reeling"
        let v1 = FlippedView(frame: NSRect(x: 0, y: 0, width: 630, height: 325))
        v1.wantsLayer = true
        v1.layer?.backgroundColor = bgMain.cgColor
        tab1.view = v1
        buildTab1(in: v1)
        tabView.addTabViewItem(tab1)

        let tab2 = NSTabViewItem(identifier: "tab2")
        tab2.label = "2. Casting Settings"
        let v2 = FlippedView(frame: NSRect(x: 0, y: 0, width: 630, height: 325))
        v2.wantsLayer = true
        v2.layer?.backgroundColor = bgMain.cgColor
        tab2.view = v2
        buildTab2(in: v2)
        tabView.addTabViewItem(tab2)

        let tab3 = NSTabViewItem(identifier: "tab3")
        tab3.label = "3. Treadmill & Camera Anti-AFK"
        let v3 = FlippedView(frame: NSRect(x: 0, y: 0, width: 630, height: 325))
        v3.wantsLayer = true
        v3.layer?.backgroundColor = bgMain.cgColor
        tab3.view = v3
        buildTab3(in: v3)
        tabView.addTabViewItem(tab3)

        // Activity Log
        root.addSubview(makeLabel("Activity Log:", x: 18, y: 520, w: 200, size: 12, bold: true, color: fgText))
        root.addSubview(makeButton("Clear Log", x: 575, y: 516, w: 90, h: 24, bg: bgInput, fg: .white, action: #selector(onClearLog)))

        let scroll = NSScrollView(frame: NSRect(x: 15, y: 545, width: 650, height: 195))
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder

        txtLog = NSTextView(frame: NSRect(x: 0, y: 0, width: 635, height: 195))
        txtLog.isEditable = false
        txtLog.backgroundColor = NSColor(calibratedRed: 0.067, green: 0.067, blue: 0.106, alpha: 1.0)
        txtLog.textColor = colGreen
        txtLog.font = NSFont(name: "Menlo", size: 11) ?? NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)
        txtLog.autoresizingMask = [.width]
        scroll.documentView = txtLog
        root.addSubview(scroll)

        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func buildTab1(in v: NSView) {
        var y: CGFloat = 20
        v.addSubview(makeLabel("Bite Timeout (Max wait in water before recasting):", x: 20, y: y + 4, w: 310))
        txtTimeout = makeInput("9.00", x: 340, y: y, w: 75)
        v.addSubview(txtTimeout)
        v.addSubview(makeLabel("seconds (Default: 9.00s)", x: 425, y: y + 4, w: 195))

        y += 40
        v.addSubview(makeLabel("Max Click Safety Cap (Stops instantly when caught):", x: 20, y: y + 4, w: 310))
        txtMaxClicks = makeInput("48", x: 340, y: y, w: 75)
        v.addSubview(txtMaxClicks)
        v.addSubview(makeLabel("max clicks", x: 425, y: y + 4, w: 150))

        y += 40
        v.addSubview(makeLabel("Click Speed (ms delay between taps):", x: 20, y: y + 4, w: 310))
        txtClickSpeedMs = makeInput("30", x: 340, y: y, w: 75)
        v.addSubview(txtClickSpeedMs)
        v.addSubview(makeLabel("ms (Default: 30ms)", x: 425, y: y + 4, w: 160))

        y += 40
        v.addSubview(makeLabel("Post-Catch Wait (Wait for reward popup to clear):", x: 20, y: y + 4, w: 310))
        txtPostCatchWait = makeInput("2.50", x: 340, y: y, w: 75)
        v.addSubview(txtPostCatchWait)
        v.addSubview(makeLabel("seconds", x: 425, y: y + 4, w: 150))

        y += 46
        v.addSubview(makeButton("Instant Test 20 Clicks (F7)", x: 20, y: y, w: 250, h: 34, bg: colYellow, fg: .black, action: #selector(onTestClicks)))
        v.addSubview(makeButton("Test Radar Scan Right Now", x: 290, y: y, w: 240, h: 34, bg: colBlue, fg: .black, action: #selector(onTestRadar)))

        y += 48
        let note = makeLabel(
            "macOS Tip: Grant Accessibility & Screen Recording permissions in System Settings.\n" +
            "Use 'Pick Point' on Tab 2 once to match your Mac's Roblox window water coordinates.",
            x: 20, y: y, w: 595, size: 11.5, bold: false, color: colGreen
        )
        note.maximumNumberOfLines = 3
        note.frame.size.height = 55
        v.addSubview(note)
    }

    private func buildTab2(in v: NSView) {
        var y: CGFloat = 20
        v.addSubview(makeLabel("Cast Aim Coordinates (Water target in display points):", x: 20, y: y, w: 400))
        y += 26
        v.addSubview(makeLabel("X:", x: 20, y: y + 4, w: 20))
        txtCastX = makeInput("1138", x: 45, y: y, w: 75)
        v.addSubview(txtCastX)

        v.addSubview(makeLabel("Y:", x: 140, y: y + 4, w: 20))
        txtCastY = makeInput("362", x: 165, y: y, w: 75)
        v.addSubview(txtCastY)

        v.addSubview(makeButton("Pick Point (3s Countdown)", x: 265, y: y - 1, w: 190, h: 28, bg: bgInput, fg: .white, action: #selector(onPickPoint)))

        y += 42
        v.addSubview(makeLabel("Cast Hold Duration (1.10s = PERFECT! Charge):", x: 20, y: y + 4, w: 310))
        txtCastHold = makeInput("1.10", x: 340, y: y, w: 75)
        v.addSubview(txtCastHold)

        y += 40
        chk2xGlitch = makeCheckbox("Enable Hotbar Key Swap Glitch (Default ON: Keys 9 & 3)", checked: true, x: 20, y: y, w: 560)
        v.addSubview(chk2xGlitch)

        y += 36
        v.addSubview(makeLabel("Rod Hotbar Key:", x: 20, y: y + 4, w: 115))
        txtRodKey = makeInput("9", x: 140, y: y, w: 45)
        v.addSubview(txtRodKey)

        v.addSubview(makeLabel("Swap Item Key:", x: 215, y: y + 4, w: 110))
        txtSwapKey = makeInput("3", x: 330, y: y, w: 45)
        v.addSubview(txtSwapKey)
    }

    private func buildTab3(in v: NSView) {
        var y: CGFloat = 18
        chkRandomize = makeCheckbox("Humanize Timings (+/- random variance to avoid pattern detection)", checked: true, x: 20, y: y, w: 580)
        v.addSubview(chkRandomize)

        y += 30
        chkAntiAfk = makeCheckbox("Enable Smooth Right-Click Camera Rotate Anti-AFK (Never presses WASD!)", checked: true, x: 20, y: y, w: 590)
        v.addSubview(chkAntiAfk)

        y += 38
        v.addSubview(makeLabel("Random Camera Rotate Interval (Min / Max Sec):", x: 20, y: y + 4, w: 300))
        txtCamMinSec = makeInput("45", x: 330, y: y, w: 65)
        v.addSubview(txtCamMinSec)
        v.addSubview(makeLabel("to", x: 405, y: y + 4, w: 20))
        txtCamMaxSec = makeInput("110", x: 430, y: y, w: 65)
        v.addSubview(txtCamMaxSec)
        v.addSubview(makeLabel("sec (1-2m)", x: 505, y: y + 4, w: 90))

        y += 38
        v.addSubview(makeLabel("Camera Right-Click Drag Distance (Points):", x: 20, y: y + 4, w: 300))
        txtCamDragPx = makeInput("120", x: 330, y: y, w: 65)
        v.addSubview(txtCamDragPx)
        v.addSubview(makeLabel("pt (Swings & returns)", x: 405, y: y + 4, w: 180))

        y += 34
        chkCamArrowAssist = makeCheckbox("Also gently tap Left/Right Arrow keys (< / >) during rotation", checked: true, x: 20, y: y, w: 560)
        v.addSubview(chkCamArrowAssist)

        y += 36
        v.addSubview(makeButton("Test Smooth Camera Rotate (2s Delay)", x: 20, y: y, w: 290, h: 34, bg: colBlue, fg: .black, action: #selector(onTestCamRotate)))

        y += 46
        let note = makeLabel(
            "How to use Treadmill Speed AFK (F8):\n" +
            "Stand on your treadmill in Roblox so your character is running, then press F8.\n" +
            "Every random 45-110s, the bot smoothly rotates the camera and returns without touching WASD.",
            x: 20, y: y, w: 595, size: 11.5, bold: false, color: colTeal
        )
        note.maximumNumberOfLines = 4
        note.frame.size.height = 65
        v.addSubview(note)
    }

    // MARK: - UI Helpers
    private func makeLabel(_ text: String, x: CGFloat, y: CGFloat, w: CGFloat, size: CGFloat = 12, bold: Bool = false, color: NSColor? = nil) -> NSTextField {
        let lbl = NSTextField(labelWithString: text)
        lbl.frame = NSRect(x: x, y: y, width: w, height: 22)
        lbl.font = bold ? NSFont.boldSystemFont(ofSize: size) : NSFont.systemFont(ofSize: size)
        lbl.textColor = color ?? fgText
        return lbl
    }

    private func makeInput(_ text: String, x: CGFloat, y: CGFloat, w: CGFloat) -> NSTextField {
        let tf = NSTextField(string: text)
        tf.frame = NSRect(x: x, y: y, width: w, height: 24)
        tf.backgroundColor = bgInput
        tf.textColor = .white
        tf.isBezeled = true
        tf.font = NSFont.systemFont(ofSize: 12)
        tf.delegate = self
        return tf
    }

    private func makeButton(_ title: String, x: CGFloat, y: CGFloat, w: CGFloat, h: CGFloat, bg: NSColor, fg: NSColor, action: Selector) -> NSButton {
        let btn = NSButton(frame: NSRect(x: x, y: y, width: w, height: h))
        btn.title = title
        btn.bezelStyle = .rounded
        btn.isBordered = false
        btn.wantsLayer = true
        btn.layer?.backgroundColor = bg.cgColor
        btn.layer?.cornerRadius = 5
        btn.font = NSFont.boldSystemFont(ofSize: 12)
        btn.attributedTitle = NSAttributedString(
            string: title,
            attributes: [.foregroundColor: fg, .font: NSFont.boldSystemFont(ofSize: 12)]
        )
        btn.target = self
        btn.action = action
        return btn
    }

    private func setButtonStyle(_ btn: NSButton, title: String, bg: NSColor, fg: NSColor) {
        btn.layer?.backgroundColor = bg.cgColor
        btn.attributedTitle = NSAttributedString(
            string: title,
            attributes: [.foregroundColor: fg, .font: NSFont.boldSystemFont(ofSize: 12)]
        )
    }

    private func makeCheckbox(_ title: String, checked: Bool, x: CGFloat, y: CGFloat, w: CGFloat) -> NSButton {
        let chk = NSButton(checkboxWithTitle: title, target: self, action: #selector(onSettingChanged))
        chk.frame = NSRect(x: x, y: y, width: w, height: 22)
        chk.state = checked ? .on : .off
        chk.attributedTitle = NSAttributedString(
            string: title,
            attributes: [.foregroundColor: fgText, .font: NSFont.systemFont(ofSize: 12)]
        )
        return chk
    }

    // MARK: - Settings Reading & Persistence
    private static func parseDouble(_ text: String, fallback: Double, min minVal: Double, max maxVal: Double) -> Double {
        let normalized = text.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: ".")
        guard let v = Double(normalized), v.isFinite else { return fallback }
        return Swift.min(maxVal, Swift.max(minVal, v))
    }

    private static func parseInt(_ text: String, fallback: Int, min minVal: Int, max maxVal: Int) -> Int {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let v = Int(trimmed) else { return fallback }
        return Swift.min(maxVal, Swift.max(minVal, v))
    }

    private func readSettingsFromUI() -> BotSettings {
        let d = BotSettings.defaults
        return BotSettings(
            timeoutSec: Self.parseDouble(txtTimeout.stringValue, fallback: d.timeoutSec, min: 1.0, max: 60.0),
            maxClicks: Self.parseInt(txtMaxClicks.stringValue, fallback: d.maxClicks, min: 1, max: 200),
            clickSpeedMs: Self.parseInt(txtClickSpeedMs.stringValue, fallback: d.clickSpeedMs, min: 5, max: 500),
            postCatchWaitSec: Self.parseDouble(txtPostCatchWait.stringValue, fallback: d.postCatchWaitSec, min: 0.5, max: 15.0),
            castX: Self.parseDouble(txtCastX.stringValue, fallback: d.castX, min: 0.0, max: 10000.0),
            castY: Self.parseDouble(txtCastY.stringValue, fallback: d.castY, min: 0.0, max: 10000.0),
            castHoldSec: Self.parseDouble(txtCastHold.stringValue, fallback: d.castHoldSec, min: 0.2, max: 5.0),
            enable2xGlitch: chk2xGlitch.state == .on,
            rodKey: txtRodKey.stringValue.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? d.rodKey : txtRodKey.stringValue,
            swapKey: txtSwapKey.stringValue.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? d.swapKey : txtSwapKey.stringValue,
            randomize: chkRandomize.state == .on,
            enableAntiAfk: chkAntiAfk.state == .on,
            camMinSec: Self.parseInt(txtCamMinSec.stringValue, fallback: d.camMinSec, min: 10, max: 600),
            camMaxSec: Self.parseInt(txtCamMaxSec.stringValue, fallback: d.camMaxSec, min: 15, max: 900),
            camDragPx: Self.parseInt(txtCamDragPx.stringValue, fallback: d.camDragPx, min: 30, max: 400),
            camArrowAssist: chkCamArrowAssist.state == .on
        )
    }

    private func currentSettings() -> BotSettings {
        settingsLock.lock()
        defer { settingsLock.unlock() }
        return cachedSettings
    }

    private func syncSettingsFromUI(save: Bool) {
        let s = readSettingsFromUI()
        settingsLock.lock()
        cachedSettings = s
        settingsLock.unlock()
        if save {
            saveSettingsToDefaults(s)
        }
    }

    private func saveSettingsToDefaults(_ s: BotSettings) {
        let ud = UserDefaults.standard
        ud.set(s.timeoutSec, forKey: "timeoutSec")
        ud.set(s.maxClicks, forKey: "maxClicks")
        ud.set(s.clickSpeedMs, forKey: "clickSpeedMs")
        ud.set(s.postCatchWaitSec, forKey: "postCatchWaitSec")
        ud.set(s.castX, forKey: "castX")
        ud.set(s.castY, forKey: "castY")
        ud.set(s.castHoldSec, forKey: "castHoldSec")
        ud.set(s.enable2xGlitch, forKey: "enable2xGlitch")
        ud.set(s.rodKey, forKey: "rodKey")
        ud.set(s.swapKey, forKey: "swapKey")
        ud.set(s.randomize, forKey: "randomize")
        ud.set(s.enableAntiAfk, forKey: "enableAntiAfk")
        ud.set(s.camMinSec, forKey: "camMinSec")
        ud.set(s.camMaxSec, forKey: "camMaxSec")
        ud.set(s.camDragPx, forKey: "camDragPx")
        ud.set(s.camArrowAssist, forKey: "camArrowAssist")
    }

    private func loadSettingsFromDefaults() {
        let ud = UserDefaults.standard
        if ud.object(forKey: "timeoutSec") != nil {
            txtTimeout.stringValue = String(format: "%.2f", Swift.min(60.0, Swift.max(1.0, ud.double(forKey: "timeoutSec"))))
        }
        if ud.object(forKey: "maxClicks") != nil {
            txtMaxClicks.stringValue = "\(Swift.min(200, Swift.max(1, ud.integer(forKey: "maxClicks"))))"
        }
        if ud.object(forKey: "clickSpeedMs") != nil {
            txtClickSpeedMs.stringValue = "\(Swift.min(500, Swift.max(5, ud.integer(forKey: "clickSpeedMs"))))"
        }
        if ud.object(forKey: "postCatchWaitSec") != nil {
            txtPostCatchWait.stringValue = String(format: "%.2f", Swift.min(15.0, Swift.max(0.5, ud.double(forKey: "postCatchWaitSec"))))
        }
        if ud.object(forKey: "castX") != nil {
            txtCastX.stringValue = "\(Int(Swift.max(0.0, ud.double(forKey: "castX"))))"
        }
        if ud.object(forKey: "castY") != nil {
            txtCastY.stringValue = "\(Int(Swift.max(0.0, ud.double(forKey: "castY"))))"
        }
        if ud.object(forKey: "castHoldSec") != nil {
            txtCastHold.stringValue = String(format: "%.2f", Swift.min(5.0, Swift.max(0.2, ud.double(forKey: "castHoldSec"))))
        }
        if ud.object(forKey: "enable2xGlitch") != nil {
            chk2xGlitch.state = ud.bool(forKey: "enable2xGlitch") ? .on : .off
        }
        if let rk = ud.string(forKey: "rodKey"), !rk.isEmpty { txtRodKey.stringValue = rk }
        if let sk = ud.string(forKey: "swapKey"), !sk.isEmpty { txtSwapKey.stringValue = sk }
        if ud.object(forKey: "randomize") != nil {
            chkRandomize.state = ud.bool(forKey: "randomize") ? .on : .off
        }
        if ud.object(forKey: "enableAntiAfk") != nil {
            chkAntiAfk.state = ud.bool(forKey: "enableAntiAfk") ? .on : .off
        }
        if ud.object(forKey: "camMinSec") != nil {
            txtCamMinSec.stringValue = "\(Swift.min(600, Swift.max(10, ud.integer(forKey: "camMinSec"))))"
        }
        if ud.object(forKey: "camMaxSec") != nil {
            txtCamMaxSec.stringValue = "\(Swift.min(900, Swift.max(15, ud.integer(forKey: "camMaxSec"))))"
        }
        if ud.object(forKey: "camDragPx") != nil {
            txtCamDragPx.stringValue = "\(Swift.min(400, Swift.max(30, ud.integer(forKey: "camDragPx"))))"
        }
        if ud.object(forKey: "camArrowAssist") != nil {
            chkCamArrowAssist.state = ud.bool(forKey: "camArrowAssist") ? .on : .off
        }
    }

    func controlTextDidChange(_ obj: Notification) {
        syncSettingsFromUI(save: true)
    }

    func controlTextDidEndEditing(_ obj: Notification) {
        syncSettingsFromUI(save: true)
    }

    @objc private func onSettingChanged() {
        syncSettingsFromUI(save: true)
    }

    // MARK: - Actions & Hotkeys
    private func registerGlobalHotkeys() {
        var eventSpec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let handlerStatus = InstallEventHandler(GetApplicationEventTarget(), carbonHotKeyHandler, 1, &eventSpec, nil, nil)
        if handlerStatus != noErr {
            log("⚠️ Failed to install global hotkey handler (OSStatus \(handlerStatus)).")
        }

        let sig: OSType = 0x53414547 // 'SAEG'
        let mappings: [(UInt32, UInt32, String)] = [
            (1, UInt32(kVK_F6), "F6"),
            (2, UInt32(kVK_F7), "F7"),
            (3, UInt32(kVK_F8), "F8")
        ]
        for (id, vk, label) in mappings {
            var ref: EventHotKeyRef?
            let hkID = EventHotKeyID(signature: sig, id: id)
            let status = RegisterEventHotKey(vk, 0, hkID, GetApplicationEventTarget(), 0, &ref)
            if status != noErr {
                log("⚠️ Could not register global hotkey \(label) (OSStatus \(status)). Another app may already be using it.")
            } else {
                hotKeyRefs.append(ref)
            }
        }
    }

    private func startTimers() {
        uiTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            guard let self = self, self.isRunning else { return }
            let sec = Int(Date().timeIntervalSince(self.startTime))
            let h = sec / 3600
            let m = (sec % 3600) / 60
            let s = sec % 60
            self.lblElapsedTime.stringValue = String(format: "Elapsed Time: %02d:%02d:%02d", h, m, s)
        }

        radarTimer = Timer.scheduledTimer(withTimeInterval: 0.35, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            if self.activeMode == .fishing { return }

            self.radarBusyLock.lock()
            if self.isRadarTimerBusy {
                self.radarBusyLock.unlock()
                return
            }
            self.isRadarTimerBusy = true
            self.radarBusyLock.unlock()

            self.radarQueue.async { [weak self] in
                guard let self = self else { return }
                defer {
                    self.radarBusyLock.lock()
                    self.isRadarTimerBusy = false
                    self.radarBusyLock.unlock()
                }
                var red = 0
                let isBite = self.radar.scan(outRedCount: &red)
                self.updateRadarLabel(detected: isBite, redCount: red, force: true)
            }
        }
    }

    private func updateRadarLabel(detected: Bool, redCount: Int, force: Bool = false) {
        let now = Date()
        if !force && now.timeIntervalSince(lastRadarUiUpdate) < 0.25 { return }
        lastRadarUiUpdate = now
        DispatchQueue.main.async {
            if detected {
                self.lblRadar.stringValue = "Radar Status: [MINIGAME STOP BUTTON DETECTED! (\(redCount) red px)]"
                self.lblRadar.textColor = self.colGreen
            } else {
                self.lblRadar.stringValue = "Radar Status: Waiting for bite (\(redCount) red px in scan zone)"
                self.lblRadar.textColor = self.colTeal
            }
        }
    }

    @objc private func onToggleFishingClick() { toggleFishingBot() }
    @objc private func onToggleTreadmillClick() { toggleTreadmillBot() }
    @objc private func onClearLog() { txtLog.string = "" }
    @objc private func onTestClicks() { doTurboClickTest() }

    @objc private func onTestRadar() {
        radarQueue.async { [weak self] in
            guard let self = self else { return }
            var red = 0
            var threshold = 40
            let detected = self.radar.scan(outRedCount: &red, outThreshold: &threshold)
            let bounds = CGDisplayBounds(CGMainDisplayID())
            let msg = "STOP Button Detected: \(detected ? "YES (Minigame Active)" : "NO (Waiting)")\nRed Pixels: \(red) (Threshold: \(threshold))\nDisplay Points: \(Int(bounds.width))x\(Int(bounds.height))"
            self.log(msg.replacingOccurrences(of: "\n", with: " | "))
            DispatchQueue.main.async {
                let alert = NSAlert()
                alert.messageText = "Radar Test"
                alert.informativeText = msg
                alert.runModal()
            }
        }
    }

    @objc private func onPickPoint() {
        log("Move mouse over the fishing water in Roblox! Picking in 3 seconds...")
        DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) { [weak self] in
            guard let self = self else { return }
            let loc = CGEvent(source: nil)?.location ?? .zero
            self.txtCastX.stringValue = "\(Int(loc.x))"
            self.txtCastY.stringValue = "\(Int(loc.y))"
            self.syncSettingsFromUI(save: true)
            NSSound.beep()
            self.log("Picked Cast Target: X=\(Int(loc.x)), Y=\(Int(loc.y)) (Saved)")
        }
    }

    @objc private func onTestCamRotate() {
        log("Switch to Roblox! Testing smooth camera rotation in 2 seconds...")
        syncSettingsFromUI(save: true)
        let settings = currentSettings()
        Thread {
            Thread.sleep(forTimeInterval: 2.0)
            self.performSmoothCameraRotate(settings: settings, runId: nil)
        }.start()
    }

    func toggleFishingBot() {
        if isRunning { stopAllBots() } else { startFishingBot() }
    }

    func toggleTreadmillBot() {
        if isRunning { stopAllBots() } else { startTreadmillBot() }
    }

    private func beginRun(mode: BotMode) -> UInt64? {
        stateLock.lock()
        defer { stateLock.unlock() }
        guard !_isRunning else { return nil }
        _isRunning = true
        _activeMode = mode
        _runGeneration &+= 1
        return _runGeneration
    }

    private func isActiveRun(_ runId: UInt64) -> Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        return _isRunning && _runGeneration == runId
    }

    private func sleepInterruptibly(_ seconds: Double, runId: UInt64) -> Bool {
        if seconds <= 0 { return isActiveRun(runId) }
        let deadline = Date().addingTimeInterval(seconds)
        while isActiveRun(runId) {
            let remaining = deadline.timeIntervalSinceNow
            if remaining <= 0 { return true }
            Thread.sleep(forTimeInterval: Swift.min(0.04, remaining))
        }
        return false
    }

    private func startFishingBot() {
        syncSettingsFromUI(save: true)
        guard let runId = beginRun(mode: .fishing) else { return }

        startTime = Date()
        setButtonStyle(btnToggleFishing, title: "STOP FISHING (F6)", bg: colRed, fg: .black)
        btnToggleTreadmill.isEnabled = false
        lblFishCount.stringValue = "Fish Caught: \(fishCaught)"
        lblStatus.stringValue = "STATUS: FISHING RUNNING (Press F6 to Stop)"
        lblStatus.textColor = colGreen
        log("Fishing Bot STARTED! Casting in 1.5s...")
        NSSound.beep()

        let t = Thread { [weak self] in self?.fishingLoop(runId: runId) }
        workerThread = t
        t.start()
    }

    private func startTreadmillBot() {
        syncSettingsFromUI(save: true)
        guard let runId = beginRun(mode: .treadmill) else { return }

        startTime = Date()
        setButtonStyle(btnToggleTreadmill, title: "STOP TREADMILL (F8)", bg: colRed, fg: .black)
        btnToggleFishing.isEnabled = false
        lblFishCount.stringValue = "Cam Rotates: \(camRotationsDone)"
        lblStatus.stringValue = "STATUS: TREADMILL AFK RUNNING (Press F8 to Stop)"
        lblStatus.textColor = colBlue
        log("Treadmill Speed AFK Mode STARTED! Stand on treadmill in Roblox.")
        NSSound.beep()

        let t = Thread { [weak self] in self?.treadmillLoop(runId: runId) }
        workerThread = t
        t.start()
    }

    private func stopAllBots() {
        stateLock.lock()
        guard _isRunning else {
            stateLock.unlock()
            return
        }
        _isRunning = false
        _activeMode = .idle
        _runGeneration &+= 1
        stateLock.unlock()

        setButtonStyle(btnToggleFishing, title: "START FISHING (F6)", bg: colGreen, fg: .black)
        btnToggleFishing.isEnabled = true
        setButtonStyle(btnToggleTreadmill, title: "TREADMILL AFK (F8)", bg: colBlue, fg: .black)
        btnToggleTreadmill.isEnabled = true
        lblStatus.stringValue = "STATUS: STOPPED (F6 = Fishing | F8 = Treadmill)"
        lblStatus.textColor = colYellow
        log("Bot STOPPED.")
        NSSound.beep()
    }

    func doTurboClickTest() {
        stateLock.lock()
        if _isTestClicking {
            stateLock.unlock()
            return
        }
        _isTestClicking = true
        stateLock.unlock()

        log("Sending 20 TURBO clicks instantly right now!")
        Thread { [weak self] in
            defer {
                if let self = self {
                    self.stateLock.lock()
                    self._isTestClicking = false
                    self.stateLock.unlock()
                }
            }
            let pt = CGEvent(source: nil)?.location ?? .zero
            for _ in 0..<20 {
                Self.postLeftMouse(down: true, at: pt)
                Thread.sleep(forTimeInterval: 0.010)
                Self.postLeftMouse(down: false, at: pt)
                Thread.sleep(forTimeInterval: 0.018)
            }
            DispatchQueue.main.async {
                NSSound.beep()
                self?.log("20 Turbo clicks finished!")
            }
        }.start()
    }

    // MARK: - Bot Loops
    private func treadmillLoop(runId: UInt64) {
        guard sleepInterruptibly(2.0, runId: runId) else { return }
        if isActiveRun(runId) {
            setStatus("STATUS: Treadmill Mode - Rotating Camera...", color: colGreen)
            performSmoothCameraRotate(settings: currentSettings(), runId: runId)
            updateCountLabel("Cam Rotates: \(camRotationsDone)")
        }

        while isActiveRun(runId) {
            let settings = currentSettings()
            let maxS = max(settings.camMinSec, settings.camMaxSec)
            let waitSec = Int.random(in: settings.camMinSec...maxS)
            log("Staying on treadmill (zero WASD). Next camera rotate in \(waitSec)s...")

            let target = Date().addingTimeInterval(TimeInterval(waitSec))
            while isActiveRun(runId) && Date() < target {
                let rem = max(0, Int(ceil(target.timeIntervalSinceNow)))
                setStatus("STATUS: TREADMILL AFK (Next Camera Rotate in \(rem)s)", color: colBlue)
                _ = sleepInterruptibly(0.5, runId: runId)
            }
            guard isActiveRun(runId) else { break }

            setStatus("STATUS: Treadmill Mode - Rotating Camera...", color: colGreen)
            performSmoothCameraRotate(settings: currentSettings(), runId: runId)
            updateCountLabel("Cam Rotates: \(camRotationsDone)")
        }
    }

    private func fishingLoop(runId: UInt64) {
        guard sleepInterruptibly(1.5, runId: runId) else { return }
        var nextCamAt = Date().addingTimeInterval(TimeInterval(randomCamInterval(currentSettings())))

        while isActiveRun(runId) {
            let settings = currentSettings()

            var initialRed = 0
            let initialDetected = radar.scan(outRedCount: &initialRed)
            updateRadarLabel(detected: initialDetected, redCount: initialRed)
            if initialDetected {
                let defaultPt = CGPoint(x: settings.castX, y: settings.castY)
                reelInFish(settings: settings, clickPt: defaultPt, runId: runId)
                continue
            }

            if settings.enableAntiAfk && Date() >= nextCamAt {
                setStatus("STATUS: Anti-AFK Smooth Camera Rotate...", color: colTeal)
                performSmoothCameraRotate(settings: settings, runId: runId)
                nextCamAt = Date().addingTimeInterval(TimeInterval(randomCamInterval(settings)))
                guard sleepInterruptibly(0.35, runId: runId) else { break }
            }
            guard isActiveRun(runId) else { break }

            setStatus(String(format: "STATUS: Charging Cast (%.2fs)...", settings.castHoldSec), color: fgText)
            if settings.enable2xGlitch {
                let rodVK = keyCode(for: settings.rodKey, fallback: UInt16(kVK_ANSI_9))
                let swapVK = keyCode(for: settings.swapKey, fallback: UInt16(kVK_ANSI_3))
                Self.pressKey(rodVK, holdMs: 50)
                guard sleepInterruptibly(0.15, runId: runId) else { break }
                Self.pressKey(swapVK, holdMs: 50)
                guard sleepInterruptibly(1.45, runId: runId) else { break }
            }
            guard isActiveRun(runId) else { break }

            var aimX = settings.castX
            var aimY = settings.castY
            if settings.randomize {
                aimX += Double(Int.random(in: -3...3))
                aimY += Double(Int.random(in: -3...3))
            }
            let aimPt = CGPoint(x: aimX, y: aimY)
            CGWarpMouseCursorPosition(aimPt)
            guard sleepInterruptibly(0.08, runId: runId) else { break }

            var holdMs = Int(settings.castHoldSec * 1000.0)
            if settings.randomize { holdMs = max(150, holdMs + Int.random(in: -25...25)) }

            Self.postLeftMouse(down: true, at: aimPt)
            let holdCompleted = sleepInterruptibly(Double(holdMs) / 1000.0, runId: runId)
            Self.postLeftMouse(down: false, at: aimPt)
            guard holdCompleted else { break }

            log("Casted rod (\(holdMs)ms hold). Waiting up to \(String(format: "%.2f", settings.timeoutSec))s for bite...")
            setStatus("STATUS: Line in Water - Waiting for Bite...", color: colYellow)
            guard sleepInterruptibly(1.0, runId: runId) else { break }

            let waitStart = Date()
            var biteDetected = false
            while isActiveRun(runId) && Date().timeIntervalSince(waitStart) < settings.timeoutSec {
                var r = 0
                let hit1 = radar.scan(outRedCount: &r)
                updateRadarLabel(detected: hit1, redCount: r)
                if hit1 {
                    guard sleepInterruptibly(0.025, runId: runId) else { break }
                    let hit2 = radar.scan(outRedCount: &r)
                    updateRadarLabel(detected: hit2, redCount: r)
                    if hit2 {
                        biteDetected = true
                        break
                    }
                }
                guard sleepInterruptibly(0.025, runId: runId) else { break }
            }
            guard isActiveRun(runId) else { break }

            if biteDetected {
                reelInFish(settings: settings, clickPt: aimPt, runId: runId)
            } else {
                log("No bite after timeout. Pulling bobber out of water to reset...")
                setStatus("STATUS: Resetting Line...", color: colRed)
                Self.postLeftMouse(down: true, at: aimPt)
                Thread.sleep(forTimeInterval: 0.05)
                Self.postLeftMouse(down: false, at: aimPt)
                _ = sleepInterruptibly(1.3, runId: runId)
            }
        }
    }

    private func reelInFish(settings: BotSettings, clickPt: CGPoint, runId: UInt64) {
        setStatus("STATUS: BITE DETECTED! TURBO REELING!", color: colGreen)
        log("Bite confirmed! Turbo clicking while STOP button is active...")

        var clicks = 0
        while isActiveRun(runId) && clicks < settings.maxClicks {
            if clicks >= 4 {
                var r = 0
                let hit1 = radar.scan(outRedCount: &r)
                updateRadarLabel(detected: hit1, redCount: r)
                if !hit1 {
                    guard sleepInterruptibly(0.025, runId: runId) else { break }
                    let hit2 = radar.scan(outRedCount: &r)
                    updateRadarLabel(detected: hit2, redCount: r)
                    if !hit2 { break }
                }
            }
            Self.postLeftMouse(down: true, at: clickPt)
            Thread.sleep(forTimeInterval: 0.010)
            Self.postLeftMouse(down: false, at: clickPt)
            clicks += 1
            guard sleepInterruptibly(Double(settings.clickSpeedMs) / 1000.0, runId: runId) else { break }
        }

        guard isActiveRun(runId) else { return }

        fishCaught += 1
        updateCountLabel("Fish Caught: \(fishCaught)")
        setStatus("STATUS: Fish Caught! Waiting for reward popup...", color: colTeal)
        let waitMs = Int(settings.postCatchWaitSec * 1000.0)
        log("Caught Fish #\(fishCaught) in \(clicks) clicks! Waiting \(waitMs)ms...")
        _ = sleepInterruptibly(settings.postCatchWaitSec, runId: runId)
    }

    // MARK: - Smooth Right-Click Camera Rotate
    private func performSmoothCameraRotate(settings: BotSettings, runId: UInt64?) {
        let shouldContinue: () -> Bool = {
            if let id = runId { return self.isActiveRun(id) }
            return true
        }
        let pauseStep: (Double) -> Bool = { secs in
            if let id = runId { return self.sleepInterruptibly(secs, runId: id) }
            Thread.sleep(forTimeInterval: secs)
            return true
        }

        guard shouldContinue() else { return }

        let bounds = CGDisplayBounds(CGMainDisplayID())
        let startX = bounds.midX + Double(Int.random(in: -35...35))
        let startY = bounds.height * 0.38 + Double(Int.random(in: -20...20))
        var curPt = CGPoint(x: startX, y: startY)

        CGWarpMouseCursorPosition(curPt)
        guard pauseStep(0.06) else { return }

        var totalPx = settings.camDragPx
        if settings.randomize {
            totalPx = max(40, totalPx + Int.random(in: -25...35))
        }
        let dir = Bool.random() ? 1 : -1
        let steps = 16
        let stepDx = Double((totalPx / steps) * dir)
        let stepDy = Double(Int.random(in: -1...1))

        Self.postRightMouse(type: .rightMouseDown, at: curPt, dx: 0, dy: 0)
        var rightDown = true
        defer {
            if rightDown {
                Self.postRightMouse(type: .rightMouseUp, at: curPt, dx: 0, dy: 0)
            }
        }

        guard pauseStep(0.055) else { return }

        var walkedSteps = 0
        for _ in 0..<steps {
            guard shouldContinue() else { break }
            curPt.x += stepDx
            curPt.y += stepDy
            Self.postRightMouse(type: .rightMouseDragged, at: curPt, dx: Int64(stepDx), dy: Int64(stepDy))
            walkedSteps += 1
            if !pauseStep(0.014) { break }
        }

        if shouldContinue() {
            _ = pauseStep(Double(Int.random(in: 120...250)) / 1000.0)
        }

        for _ in 0..<walkedSteps {
            curPt.x -= stepDx
            curPt.y -= stepDy
            Self.postRightMouse(type: .rightMouseDragged, at: curPt, dx: Int64(-stepDx), dy: Int64(-stepDy))
            Thread.sleep(forTimeInterval: 0.014)
        }

        Thread.sleep(forTimeInterval: 0.045)
        Self.postRightMouse(type: .rightMouseUp, at: curPt, dx: 0, dy: 0)
        rightDown = false

        guard shouldContinue() else { return }

        if settings.camArrowAssist {
            guard pauseStep(0.08) else { return }
            let first = dir > 0 ? UInt16(kVK_RightArrow) : UInt16(kVK_LeftArrow)
            let second = dir > 0 ? UInt16(kVK_LeftArrow) : UInt16(kVK_RightArrow)
            let hold = Int.random(in: 85...140)
            Self.pressKey(first, holdMs: hold)
            guard pauseStep(0.09) else { return }
            Self.pressKey(second, holdMs: hold)
        }

        camRotationsDone += 1
        log("Camera Rotate #\(camRotationsDone) complete (\(abs(Int(stepDx) * steps))pt sweep & return).")
    }

    private func randomCamInterval(_ s: BotSettings) -> Int {
        let maxS = max(s.camMinSec, s.camMaxSec)
        return Int.random(in: s.camMinSec...maxS)
    }

    // MARK: - Low-level CGEvent Helpers
    private static func postLeftMouse(down: Bool, at pt: CGPoint) {
        let type: CGEventType = down ? .leftMouseDown : .leftMouseUp
        CGEvent(mouseEventSource: nil, mouseType: type, mouseCursorPosition: pt, mouseButton: .left)?
            .post(tap: .cghidEventTap)
    }

    private static func postRightMouse(type: CGEventType, at pt: CGPoint, dx: Int64, dy: Int64) {
        let ev = CGEvent(mouseEventSource: nil, mouseType: type, mouseCursorPosition: pt, mouseButton: .right)
        ev?.setIntegerValueField(.mouseEventDeltaX, value: dx)
        ev?.setIntegerValueField(.mouseEventDeltaY, value: dy)
        ev?.post(tap: .cghidEventTap)
    }

    private static func pressKey(_ vk: CGKeyCode, holdMs: Int) {
        CGEvent(keyboardEventSource: nil, virtualKey: vk, keyDown: true)?.post(tap: .cghidEventTap)
        Thread.sleep(forTimeInterval: Double(holdMs) / 1000.0)
        CGEvent(keyboardEventSource: nil, virtualKey: vk, keyDown: false)?.post(tap: .cghidEventTap)
    }

    private func keyCode(for text: String, fallback: UInt16) -> UInt16 {
        guard let ch = text.trimmingCharacters(in: .whitespacesAndNewlines).uppercased().first else { return fallback }
        let map: [Character: Int] = [
            "0": kVK_ANSI_0, "1": kVK_ANSI_1, "2": kVK_ANSI_2, "3": kVK_ANSI_3, "4": kVK_ANSI_4,
            "5": kVK_ANSI_5, "6": kVK_ANSI_6, "7": kVK_ANSI_7, "8": kVK_ANSI_8, "9": kVK_ANSI_9,
            "A": kVK_ANSI_A, "B": kVK_ANSI_B, "C": kVK_ANSI_C, "D": kVK_ANSI_D, "E": kVK_ANSI_E,
            "F": kVK_ANSI_F, "G": kVK_ANSI_G, "H": kVK_ANSI_H, "I": kVK_ANSI_I, "J": kVK_ANSI_J,
            "K": kVK_ANSI_K, "L": kVK_ANSI_L, "M": kVK_ANSI_M, "N": kVK_ANSI_N, "O": kVK_ANSI_O,
            "P": kVK_ANSI_P, "Q": kVK_ANSI_Q, "R": kVK_ANSI_R, "S": kVK_ANSI_S, "T": kVK_ANSI_T,
            "U": kVK_ANSI_U, "V": kVK_ANSI_V, "W": kVK_ANSI_W, "X": kVK_ANSI_X, "Y": kVK_ANSI_Y,
            "Z": kVK_ANSI_Z
        ]
        if let code = map[ch] { return UInt16(code) }
        return fallback
    }

    // MARK: - Thread-safe UI Updates
    private func setStatus(_ text: String, color: NSColor) {
        DispatchQueue.main.async {
            self.lblStatus.stringValue = text
            self.lblStatus.textColor = color
        }
    }

    private func updateCountLabel(_ text: String) {
        DispatchQueue.main.async {
            self.lblFishCount.stringValue = text
        }
    }

    private func log(_ message: String) {
        let line = "[\(logDateFormatter.string(from: Date()))] \(message)\n"
        DispatchQueue.main.async {
            guard let storage = self.txtLog.textStorage else { return }
            if storage.length > 12000 {
                storage.deleteCharacters(in: NSRange(location: 0, length: storage.length - 6000))
            }
            let attrs: [NSAttributedString.Key: Any] = [
                .foregroundColor: self.colGreen,
                .font: self.txtLog.font ?? NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)
            ]
            storage.append(NSAttributedString(string: line, attributes: attrs))
            self.txtLog.scrollToEndOfDocument(nil)
        }
    }
}

// MARK: - App Entry Point
let app = NSApplication.shared
app.setActivationPolicy(.regular)
if #available(macOS 14.0, *) {
    let delegate = BotAppController()
    app.delegate = delegate
    app.run()
} else {
    let alert = NSAlert()
    alert.messageText = "macOS 14.0+ Required"
    alert.informativeText = "This app uses Apple ScreenCaptureKit, which requires macOS 14 Sonoma or newer."
    alert.runModal()
}
