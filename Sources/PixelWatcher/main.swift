import AppKit
import ApplicationServices
import CoreGraphics

final class AppDelegate: NSObject, NSApplicationDelegate, NSTextFieldDelegate {
    private var window: NSWindow!
    private var positionLabel: NSTextField!
    private var modePopup: NSPopUpButton!
    private var targetSection: NSStackView!
    private var colorScrollView: NSScrollView!
    private var colorDocumentView: FlippedView!
    private var colorListStack: NSStackView!
    private var addColorButton: NSButton!
    private var toleranceField: NSTextField!
    private var toleranceStepper: NSStepper!
    private var clickLocationPopup: NSPopUpButton!
    private var currentColorLabel: NSTextField!
    private var selectButton: NSButton!
    private var startButton: NSButton!
    private var statusLabel: NSTextField!

    private var colorRows: [ColorRow] = []
    private var activeTargets: [ColorTarget] = []
    private var selectedPoint: CGPoint?
    private var baselineColor: PixelColor?
    private var pollTimer: DispatchSourceTimer?
    private var countdownTimer: DispatchSourceTimer?
    private var escapeWatchdog: DispatchSourceTimer?
    private var pendingClick: DispatchWorkItem?
    private var localKeyMonitor: Any?
    private var globalKeyMonitor: Any?
    private var isSelecting = false
    private var pickingColorRowID: UUID?
    private var isCountingDown = false
    private var countdownRemaining = 0
    private var isMonitoring = false
    private var armedForChange = true
    private var lastMatchedTargetID: UUID?
    private var lastChangeTime = Date.distantPast
    private var lastColorDisplay = Date.distantPast

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildUI()
        installKeyMonitors()
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationWillTerminate(_ notification: Notification) {
        stopMonitoring(message: "已停止")
        if let localKeyMonitor { NSEvent.removeMonitor(localKeyMonitor) }
        if let globalKeyMonitor { NSEvent.removeMonitor(globalKeyMonitor) }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    private func buildUI() {
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 470, height: 545),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "颜色监控点击器"
        window.center()
        window.isReleasedWhenClosed = false

        let root = NSStackView()
        root.orientation = .vertical
        root.alignment = .leading
        root.spacing = 12
        root.edgeInsets = NSEdgeInsets(top: 18, left: 22, bottom: 18, right: 22)
        root.translatesAutoresizingMaskIntoConstraints = false

        let content = NSView()
        window.contentView = content
        content.addSubview(root)
        NSLayoutConstraint.activate([
            root.leadingAnchor.constraint(equalTo: content.leadingAnchor),
            root.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            root.topAnchor.constraint(equalTo: content.topAnchor),
            root.bottomAnchor.constraint(lessThanOrEqualTo: content.bottomAnchor)
        ])

        let title = NSTextField(labelWithString: "屏幕颜色触发点击")
        title.font = .systemFont(ofSize: 18, weight: .semibold)
        root.addArrangedSubview(title)

        let positionRow = NSStackView()
        positionRow.orientation = .horizontal
        positionRow.spacing = 10
        selectButton = NSButton(title: "选择位置", target: self, action: #selector(beginSelecting))
        selectButton.bezelStyle = .rounded
        positionRow.addArrangedSubview(selectButton)
        positionLabel = NSTextField(labelWithString: "尚未选择")
        positionLabel.textColor = .secondaryLabelColor
        positionRow.addArrangedSubview(positionLabel)
        root.addArrangedSubview(positionRow)

        let modeRow = makeLabeledRow("监控功能")
        modePopup = NSPopUpButton()
        modePopup.addItems(withTitles: ["匹配目标颜色", "检测像素变化"])
        modePopup.target = self
        modePopup.action = #selector(modeChanged)
        modeRow.addArrangedSubview(modePopup)
        root.addArrangedSubview(modeRow)

        buildTargetSection()
        root.addArrangedSubview(targetSection)

        let toleranceRow = makeLabeledRow("颜色容差")
        toleranceField = NSTextField(labelWithString: "10")
        toleranceField.alignment = .right
        toleranceField.font = .monospacedSystemFont(ofSize: 13, weight: .regular)
        toleranceField.widthAnchor.constraint(equalToConstant: 35).isActive = true
        toleranceRow.addArrangedSubview(toleranceField)
        toleranceStepper = NSStepper()
        toleranceStepper.minValue = 0
        toleranceStepper.maxValue = 100
        toleranceStepper.integerValue = 10
        toleranceStepper.target = self
        toleranceStepper.action = #selector(toleranceChanged)
        toleranceRow.addArrangedSubview(toleranceStepper)
        let toleranceHint = NSTextField(labelWithString: "RGB 每个通道允许 ±10")
        toleranceHint.textColor = .secondaryLabelColor
        toleranceHint.tag = 101
        toleranceRow.addArrangedSubview(toleranceHint)
        root.addArrangedSubview(toleranceRow)

        let clickRow = makeLabeledRow("点击位置")
        clickLocationPopup = NSPopUpButton()
        clickLocationPopup.addItems(withTitles: ["选择的监控位置", "当前鼠标位置"])
        clickRow.addArrangedSubview(clickLocationPopup)
        root.addArrangedSubview(clickRow)

        currentColorLabel = NSTextField(labelWithString: "当前位置颜色：—")
        currentColorLabel.font = .monospacedSystemFont(ofSize: 12, weight: .regular)
        currentColorLabel.textColor = .secondaryLabelColor
        root.addArrangedSubview(currentColorLabel)

        startButton = NSButton(title: "开始监控", target: self, action: #selector(toggleMonitoring))
        startButton.bezelStyle = .rounded
        startButton.widthAnchor.constraint(equalToConstant: 140).isActive = true
        root.addArrangedSubview(startButton)

        statusLabel = NSTextField(wrappingLabelWithString: "点击“选择位置”，移动鼠标后按 Enter 确认")
        statusLabel.textColor = .secondaryLabelColor
        statusLabel.maximumNumberOfLines = 2
        root.addArrangedSubview(statusLabel)
    }

    private func buildTargetSection() {
        targetSection = NSStackView()
        targetSection.orientation = .vertical
        targetSection.alignment = .leading
        targetSection.spacing = 6

        let header = NSStackView()
        header.orientation = .horizontal
        header.spacing = 8
        let colorHeader = NSTextField(labelWithString: "目标颜色")
        colorHeader.widthAnchor.constraint(equalToConstant: 235).isActive = true
        header.addArrangedSubview(colorHeader)
        let delayHeader = NSTextField(labelWithString: "延时（毫秒）")
        delayHeader.textColor = .secondaryLabelColor
        header.addArrangedSubview(delayHeader)
        targetSection.addArrangedSubview(header)

        let scroll = NSScrollView()
        colorScrollView = scroll
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.borderType = .bezelBorder
        scroll.widthAnchor.constraint(equalToConstant: 424).isActive = true
        scroll.heightAnchor.constraint(equalToConstant: 145).isActive = true

        let document = FlippedView(frame: NSRect(x: 0, y: 0, width: 410, height: 145))
        colorDocumentView = document
        colorListStack = NSStackView(frame: NSRect(x: 7, y: 7, width: 396, height: 131))
        colorListStack.orientation = .vertical
        colorListStack.alignment = .leading
        colorListStack.spacing = 5
        document.addSubview(colorListStack)
        scroll.documentView = document
        targetSection.addArrangedSubview(scroll)

        addColorButton = NSButton(title: "＋ 添加颜色", target: self, action: #selector(addColorRowAction))
        addColorButton.bezelStyle = .rounded
        targetSection.addArrangedSubview(addColorButton)
        addColorRow(hex: "#66D169", delay: "0")
    }

    private func makeLabeledRow(_ title: String) -> NSStackView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.spacing = 10
        let label = NSTextField(labelWithString: title)
        label.widthAnchor.constraint(equalToConstant: 70).isActive = true
        row.addArrangedSubview(label)
        return row
    }

    @objc private func addColorRowAction() {
        addColorRow(hex: "#FFFFFF", delay: "0")
    }

    private func addColorRow(hex: String, delay: String) {
        let id = UUID()
        let container = NSStackView()
        container.orientation = .horizontal
        container.spacing = 7

        let numberLabel = NSTextField(labelWithString: "颜色 \(colorRows.count + 1)")
        numberLabel.widthAnchor.constraint(equalToConstant: 52).isActive = true
        container.addArrangedSubview(numberLabel)

        let colorField = NSTextField(string: hex)
        colorField.placeholderString = "#RRGGBB"
        colorField.font = .monospacedSystemFont(ofSize: 12, weight: .regular)
        colorField.delegate = self
        colorField.widthAnchor.constraint(equalToConstant: 92).isActive = true
        container.addArrangedSubview(colorField)

        let colorWell = NSColorWell(frame: NSRect(x: 0, y: 0, width: 30, height: 24))
        colorWell.isEnabled = false
        colorWell.widthAnchor.constraint(equalToConstant: 30).isActive = true
        container.addArrangedSubview(colorWell)

        let pickButton = NSButton(title: "吸取", target: self, action: #selector(beginPickingColor(_:)))
        pickButton.bezelStyle = .rounded
        pickButton.identifier = NSUserInterfaceItemIdentifier(id.uuidString)
        pickButton.widthAnchor.constraint(equalToConstant: 48).isActive = true
        container.addArrangedSubview(pickButton)

        let delayField = NSTextField(string: delay)
        delayField.placeholderString = "0"
        delayField.font = .monospacedSystemFont(ofSize: 12, weight: .regular)
        delayField.alignment = .right
        delayField.widthAnchor.constraint(equalToConstant: 72).isActive = true
        container.addArrangedSubview(delayField)

        let msLabel = NSTextField(labelWithString: "ms")
        msLabel.textColor = .secondaryLabelColor
        container.addArrangedSubview(msLabel)

        let deleteButton = NSButton(title: "−", target: self, action: #selector(deleteColorRow(_:)))
        deleteButton.bezelStyle = .circular
        deleteButton.identifier = NSUserInterfaceItemIdentifier(id.uuidString)
        container.addArrangedSubview(deleteButton)

        let row = ColorRow(
            id: id,
            container: container,
            numberLabel: numberLabel,
            colorField: colorField,
            colorWell: colorWell,
            pickButton: pickButton,
            delayField: delayField,
            deleteButton: deleteButton
        )
        colorRows.append(row)
        colorListStack.addArrangedSubview(container)
        updateColorWell(for: row)
        refreshColorRows()
        updateColorListLayout(scrollToBottom: colorRows.count > 1)
        if statusLabel != nil, colorRows.count > 1 {
            statusLabel.stringValue = "已添加颜色 \(colorRows.count)"
        }
    }

    @objc private func deleteColorRow(_ sender: NSButton) {
        guard colorRows.count > 1,
              let rawID = sender.identifier?.rawValue,
              let index = colorRows.firstIndex(where: { $0.id.uuidString == rawID }) else {
            NSSound.beep()
            statusLabel.stringValue = "至少需要保留一个目标颜色"
            return
        }
        let row = colorRows.remove(at: index)
        colorListStack.removeArrangedSubview(row.container)
        row.container.removeFromSuperview()
        refreshColorRows()
        updateColorListLayout(scrollToBottom: false)
        statusLabel.stringValue = "已删除，当前共有 \(colorRows.count) 个目标颜色"
    }

    @objc private func beginPickingColor(_ sender: NSButton) {
        guard let rawID = sender.identifier?.rawValue,
              let row = colorRows.first(where: { $0.id.uuidString == rawID }) else { return }
        stopMonitoring(message: "")
        cancelPositionSelection()
        cancelColorPicking()
        pickingColorRowID = row.id
        row.pickButton.title = "等待…"
        statusLabel.stringValue = "请把鼠标移到要吸取的颜色上，然后按 Enter；按 Esc 取消"
    }

    private func confirmColorPicking() {
        guard let id = pickingColorRowID,
              let row = colorRows.first(where: { $0.id == id }),
              let point = CGEvent(source: nil)?.location else {
            cancelColorPicking()
            return
        }
        guard let color = sampleColor(at: point) else {
            cancelColorPicking()
            requestScreenPermission()
            return
        }
        row.colorField.stringValue = color.hex
        row.colorWell.color = color.nsColor
        cancelColorPicking()
        statusLabel.stringValue = "已吸取颜色 \(color.hex)"
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func cancelColorPicking() {
        if let id = pickingColorRowID,
           let row = colorRows.first(where: { $0.id == id }) {
            row.pickButton.title = "吸取"
        }
        pickingColorRowID = nil
    }

    private func cancelPositionSelection() {
        if isSelecting { selectButton.title = "选择位置" }
        isSelecting = false
    }

    private func refreshColorRows() {
        for (index, row) in colorRows.enumerated() {
            row.numberLabel.stringValue = "颜色 \(index + 1)"
            row.deleteButton.isEnabled = colorRows.count > 1 && !isMonitoring
        }
    }

    private func updateColorListLayout(scrollToBottom: Bool) {
        guard colorScrollView != nil, colorDocumentView != nil else { return }
        let visibleWidth = max(390, colorScrollView.contentSize.width)
        let rowsHeight = CGFloat(colorRows.count * 29 + max(0, colorRows.count - 1) * 5)
        let documentHeight = max(colorScrollView.contentSize.height, rowsHeight + 14)
        colorDocumentView.frame = NSRect(x: 0, y: 0, width: visibleWidth, height: documentHeight)
        colorListStack.frame = NSRect(x: 7, y: 7, width: visibleWidth - 14, height: rowsHeight)
        colorListStack.needsLayout = true
        colorListStack.layoutSubtreeIfNeeded()
        colorScrollView.reflectScrolledClipView(colorScrollView.contentView)
        if scrollToBottom, let last = colorRows.last {
            last.container.scrollToVisible(last.container.bounds)
        }
    }

    @objc private func modeChanged() {
        let usesTargetColors = modePopup.indexOfSelectedItem == 0
        targetSection.isHidden = !usesTargetColors
        statusLabel.stringValue = usesTargetColors
            ? "可添加多个颜色，并分别设置识别后的点击延时"
            : "开始监控时会记录当前位置颜色，变化超过容差后点击"
    }

    @objc private func toleranceChanged() {
        let value = toleranceStepper.integerValue
        toleranceField.stringValue = "\(value)"
        if let hint = toleranceStepper.superview?.viewWithTag(101) as? NSTextField {
            hint.stringValue = "RGB 每个通道允许 ±\(value)"
        }
    }

    func controlTextDidChange(_ notification: Notification) {
        guard let field = notification.object as? NSTextField,
              let row = colorRows.first(where: { $0.colorField === field }) else { return }
        updateColorWell(for: row)
    }

    private func updateColorWell(for row: ColorRow) {
        row.colorWell.color = parseHexColor(row.colorField.stringValue)?.nsColor ?? .clear
    }

    private func installKeyMonitors() {
        localKeyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self else { return event }
            return self.handleKey(event.keyCode) ? nil : event
        }
        globalKeyMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            DispatchQueue.main.async { _ = self?.handleKey(event.keyCode) }
        }
    }

    @discardableResult
    private func handleKey(_ keyCode: UInt16) -> Bool {
        if keyCode == 53 {
            cancelPositionSelection()
            cancelColorPicking()
            stopMonitoring(message: "已停止（Esc）")
            return true
        }
        if keyCode == 36 && pickingColorRowID != nil {
            confirmColorPicking()
            return true
        }
        if keyCode == 36 && isSelecting {
            confirmSelection()
            return true
        }
        return false
    }

    @objc private func beginSelecting() {
        stopMonitoring(message: "")
        cancelColorPicking()
        isSelecting = true
        selectButton.title = "等待 Enter…"
        statusLabel.stringValue = "请移动鼠标到目标位置，然后按 Enter 确认；按 Esc 取消"
    }

    private func confirmSelection() {
        guard let point = CGEvent(source: nil)?.location else {
            statusLabel.stringValue = "无法读取鼠标位置"
            return
        }
        isSelecting = false
        selectedPoint = point
        selectButton.title = "重新选择"
        positionLabel.stringValue = "x: \(Int(point.x)), y: \(Int(point.y))"
        if let color = sampleColor(at: point) {
            currentColorLabel.stringValue = "当前位置颜色：\(color.hex)"
            statusLabel.stringValue = "位置已确认"
        } else {
            requestScreenPermission()
        }
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func toggleMonitoring() {
        (isMonitoring || isCountingDown)
            ? stopMonitoring(message: "已取消")
            : prepareMonitoringCountdown()
    }

    private func prepareMonitoringCountdown() {
        guard let point = selectedPoint else {
            statusLabel.stringValue = "请先选择监控位置"
            NSSound.beep()
            return
        }
        let usesTargetColors = modePopup.indexOfSelectedItem == 0
        if usesTargetColors {
            guard let targets = parseAllTargets() else { return }
            activeTargets = targets
        } else {
            activeTargets = []
        }
        guard CGPreflightScreenCaptureAccess() else {
            requestScreenPermission()
            return
        }
        guard requestAccessibilityPermission() else {
            statusLabel.stringValue = "请允许辅助功能权限，然后重新打开本工具"
            return
        }
        guard let current = sampleColor(at: point) else {
            statusLabel.stringValue = "无法读取屏幕颜色，请检查屏幕录制权限"
            return
        }

        baselineColor = current
        isCountingDown = true
        countdownRemaining = 3
        setControlsEnabled(false)
        startButton.isEnabled = true
        startButton.title = "取消倒计时（3）"
        statusLabel.stringValue = "3 秒后开始监控… 按 Esc 取消"

        let countdown = DispatchSource.makeTimerSource(queue: .main)
        countdown.schedule(deadline: .now() + 1, repeating: .seconds(1), leeway: .milliseconds(20))
        countdown.setEventHandler { [weak self] in self?.advanceCountdown() }
        countdownTimer = countdown
        countdown.resume()
    }

    private func advanceCountdown() {
        guard isCountingDown else { return }
        if CGEventSource.keyState(.combinedSessionState, key: 53) {
            stopMonitoring(message: "已取消（Esc）")
            return
        }
        countdownRemaining -= 1
        if countdownRemaining > 0 {
            startButton.title = "取消倒计时（\(countdownRemaining)）"
            statusLabel.stringValue = "\(countdownRemaining) 秒后开始监控… 按 Esc 取消"
        } else {
            activateMonitoring()
        }
    }

    private func activateMonitoring() {
        guard let point = selectedPoint, let current = sampleColor(at: point) else {
            stopMonitoring(message: "无法读取屏幕颜色，请检查屏幕录制权限")
            return
        }
        countdownTimer?.cancel()
        countdownTimer = nil
        isCountingDown = false
        let usesTargetColors = modePopup.indexOfSelectedItem == 0

        baselineColor = current
        armedForChange = true
        lastMatchedTargetID = nil
        lastChangeTime = .distantPast
        isMonitoring = true
        setControlsEnabled(false)
        startButton.isEnabled = true
        startButton.title = "停止监控"
        statusLabel.stringValue = usesTargetColors
            ? "正在监控 \(activeTargets.count) 个目标颜色… 按 Esc 停止"
            : "正在检测像素变化，容差 ±\(toleranceStepper.integerValue)… 按 Esc 停止"

        let source = DispatchSource.makeTimerSource(queue: .main)
        source.schedule(deadline: .now(), repeating: .milliseconds(8), leeway: .milliseconds(1))
        source.setEventHandler { [weak self] in self?.pollPixel() }
        pollTimer = source
        source.resume()

        let watchdog = DispatchSource.makeTimerSource(queue: DispatchQueue.global(qos: .userInteractive))
        watchdog.schedule(deadline: .now(), repeating: .milliseconds(5), leeway: .milliseconds(1))
        watchdog.setEventHandler { [weak self] in
            if CGEventSource.keyState(.combinedSessionState, key: 53) {
                DispatchQueue.main.async { self?.stopMonitoring(message: "已停止（Esc）") }
            }
        }
        escapeWatchdog = watchdog
        watchdog.resume()
    }

    private func parseAllTargets() -> [ColorTarget]? {
        var result: [ColorTarget] = []
        for (index, row) in colorRows.enumerated() {
            guard let color = parseHexColor(row.colorField.stringValue) else {
                statusLabel.stringValue = "颜色 \(index + 1) 格式不正确，请输入例如 #66D169"
                NSSound.beep()
                return nil
            }
            let delayText = row.delayField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            guard let delay = Int(delayText), (0...60_000).contains(delay) else {
                statusLabel.stringValue = "颜色 \(index + 1) 的延时应为 0–60000 毫秒"
                NSSound.beep()
                return nil
            }
            result.append(ColorTarget(id: row.id, color: color, delayMilliseconds: delay))
        }
        return result
    }

    private func parseHexColor(_ input: String) -> PixelColor? {
        var value = input.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        if value.hasPrefix("#") { value.removeFirst() }
        guard value.count == 6, value.allSatisfy({ $0.isHexDigit }),
              let rgb = UInt32(value, radix: 16) else { return nil }
        return PixelColor(
            r: UInt8((rgb >> 16) & 0xFF),
            g: UInt8((rgb >> 8) & 0xFF),
            b: UInt8(rgb & 0xFF)
        )
    }

    private func pollPixel() {
        guard isMonitoring, let point = selectedPoint,
              let current = sampleColor(at: point) else { return }
        if CGEventSource.keyState(.combinedSessionState, key: 53) {
            stopMonitoring(message: "已停止（Esc）")
            return
        }

        if Date().timeIntervalSince(lastColorDisplay) > 0.1 {
            currentColorLabel.stringValue = "当前位置颜色：\(current.hex)"
            lastColorDisplay = Date()
        }

        let tolerance = toleranceStepper.integerValue
        if modePopup.indexOfSelectedItem == 0 {
            pollTargetColors(current: current, tolerance: tolerance)
        } else {
            pollPixelChange(current: current, tolerance: tolerance)
        }
    }

    private func pollTargetColors(current: PixelColor, tolerance: Int) {
        let matched = activeTargets.first { current.isWithin(tolerance, of: $0.color) }
        guard let matched else {
            if lastMatchedTargetID != nil {
                pendingClick?.cancel()
                pendingClick = nil
                statusLabel.stringValue = "继续等待目标颜色… 按 Esc 停止"
            }
            lastMatchedTargetID = nil
            return
        }
        guard matched.id != lastMatchedTargetID else { return }

        pendingClick?.cancel()
        pendingClick = nil
        lastMatchedTargetID = matched.id
        if matched.delayMilliseconds == 0 {
            executeTargetClick(matched)
            return
        }

        statusLabel.stringValue = "匹配 \(matched.color.hex)，保持 \(matched.delayMilliseconds) ms 后点击"
        let work = DispatchWorkItem { [weak self] in
            self?.executeTargetClick(matched)
        }
        pendingClick = work
        DispatchQueue.main.asyncAfter(
            deadline: .now() + .milliseconds(matched.delayMilliseconds),
            execute: work
        )
    }

    private func executeTargetClick(_ target: ColorTarget) {
        pendingClick = nil
        guard isMonitoring, modePopup.indexOfSelectedItem == 0,
              lastMatchedTargetID == target.id,
              let point = selectedPoint,
              let current = sampleColor(at: point),
              current.isWithin(toleranceStepper.integerValue, of: target.color),
              !CGEventSource.keyState(.combinedSessionState, key: 53) else { return }
        performSingleClick(at: resolvedClickPoint(monitoredPoint: point))
        statusLabel.stringValue = "已匹配 \(target.color.hex)，延时 \(target.delayMilliseconds) ms 后完成点击"
    }

    private func pollPixelChange(current: PixelColor, tolerance: Int) {
        guard let point = selectedPoint, let baselineColor else { return }
        let changed = !current.isWithin(tolerance, of: baselineColor)
        if changed {
            if armedForChange {
                performSingleClick(at: resolvedClickPoint(monitoredPoint: point))
                statusLabel.stringValue = "检测到像素变化，已点击；等待颜色稳定"
            }
            self.baselineColor = current
            armedForChange = false
            lastChangeTime = Date()
        } else if !armedForChange, Date().timeIntervalSince(lastChangeTime) >= 0.25 {
            armedForChange = true
            statusLabel.stringValue = "颜色已稳定，继续监控… 按 Esc 停止"
        }
    }

    private func resolvedClickPoint(monitoredPoint: CGPoint) -> CGPoint {
        clickLocationPopup.indexOfSelectedItem == 0
            ? monitoredPoint
            : (CGEvent(source: nil)?.location ?? monitoredPoint)
    }

    private func stopMonitoring(message: String) {
        pendingClick?.cancel()
        pendingClick = nil
        countdownTimer?.cancel()
        countdownTimer = nil
        pollTimer?.cancel()
        pollTimer = nil
        escapeWatchdog?.cancel()
        escapeWatchdog = nil
        isCountingDown = false
        countdownRemaining = 0
        isMonitoring = false
        lastMatchedTargetID = nil
        if window != nil {
            setControlsEnabled(true)
            startButton.title = "开始监控"
            if !message.isEmpty { statusLabel.stringValue = message }
        }
    }

    private func setControlsEnabled(_ enabled: Bool) {
        selectButton.isEnabled = enabled
        modePopup.isEnabled = enabled
        toleranceStepper.isEnabled = enabled
        clickLocationPopup.isEnabled = enabled
        addColorButton.isEnabled = enabled
        for row in colorRows {
            row.colorField.isEnabled = enabled
            row.pickButton.isEnabled = enabled
            row.delayField.isEnabled = enabled
            row.deleteButton.isEnabled = enabled && colorRows.count > 1
        }
        refreshColorRows()
    }

    private func sampleColor(at point: CGPoint) -> PixelColor? {
        let rect = CGRect(x: floor(point.x), y: floor(point.y), width: 1, height: 1)
        guard let image = CGWindowListCreateImage(rect, .optionOnScreenOnly, kCGNullWindowID, [.nominalResolution]),
              let provider = image.dataProvider,
              let data = provider.data,
              let bytes = CFDataGetBytePtr(data), image.bitsPerPixel >= 24 else { return nil }
        return PixelColor(r: bytes[2], g: bytes[1], b: bytes[0])
    }

    private func performSingleClick(at point: CGPoint) {
        let source = CGEventSource(stateID: .hidSystemState)
        let down = CGEvent(mouseEventSource: source, mouseType: .leftMouseDown, mouseCursorPosition: point, mouseButton: .left)
        let up = CGEvent(mouseEventSource: source, mouseType: .leftMouseUp, mouseCursorPosition: point, mouseButton: .left)
        down?.post(tap: .cghidEventTap)
        up?.post(tap: .cghidEventTap)
    }

    private func requestScreenPermission() {
        _ = CGRequestScreenCaptureAccess()
        statusLabel.stringValue = "请允许屏幕录制权限；授权后重新打开本工具"
    }

    private func requestAccessibilityPermission() -> Bool {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }
}

private final class ColorRow {
    let id: UUID
    let container: NSStackView
    let numberLabel: NSTextField
    let colorField: NSTextField
    let colorWell: NSColorWell
    let pickButton: NSButton
    let delayField: NSTextField
    let deleteButton: NSButton

    init(id: UUID, container: NSStackView, numberLabel: NSTextField, colorField: NSTextField,
         colorWell: NSColorWell, pickButton: NSButton, delayField: NSTextField, deleteButton: NSButton) {
        self.id = id
        self.container = container
        self.numberLabel = numberLabel
        self.colorField = colorField
        self.colorWell = colorWell
        self.pickButton = pickButton
        self.delayField = delayField
        self.deleteButton = deleteButton
    }
}

private final class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

private struct ColorTarget {
    let id: UUID
    let color: PixelColor
    let delayMilliseconds: Int
}

private struct PixelColor: Equatable {
    let r: UInt8
    let g: UInt8
    let b: UInt8

    var hex: String { String(format: "#%02X%02X%02X", r, g, b) }
    var nsColor: NSColor {
        NSColor(
            calibratedRed: CGFloat(r) / 255,
            green: CGFloat(g) / 255,
            blue: CGFloat(b) / 255,
            alpha: 1
        )
    }

    func isWithin(_ tolerance: Int, of other: PixelColor) -> Bool {
        abs(Int(r) - Int(other.r)) <= tolerance &&
        abs(Int(g) - Int(other.g)) <= tolerance &&
        abs(Int(b) - Int(other.b)) <= tolerance
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
