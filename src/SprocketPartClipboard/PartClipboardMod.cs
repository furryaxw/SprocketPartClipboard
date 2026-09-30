using System;
using MelonLoader;
using MelonLoader.Utils;
using SprocketModAPI;
using SprocketPartClipboard.Clipboard;
using SprocketPartClipboard.Designer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SprocketPartClipboard
{
    public sealed class PartClipboardMod : MelonMod
    {
        private const string CopyActionId = "copy-part-tree";
        private const string PasteActionId = "paste-part-tree";

        private ClipboardLibrary library = new ClipboardLibrary();
        private PartTreeClipboardService? service;
        private IInputActionHandle? copyAction;
        private IInputActionHandle? pasteAction;
        private int lastDispatchFrame = -1;

        public override void OnInitializeMelon()
        {
            string path = ClipboardLibraryStore.DefaultFilePath(MelonEnvironment.UserDataDirectory);
            ClipboardLibraryStore store = new ClipboardLibraryStore(path);
            ClipboardLoadResult loaded = store.Load();

            library = loaded.Library;
            service = new PartTreeClipboardService(store, library);

            if (loaded.RecoveredFromCorruption)
                LoggerInstance.Warning($"[PartClipboard] 剪贴板文件无法解析，已保留为 {loaded.BackupPath}，本次从空剪贴板开始。");
            else if (loaded.Error != null)
                LoggerInstance.Warning($"[PartClipboard] 剪贴板文件读取失败：{loaded.Error}");

            RegisterActions();
            LoggerInstance.Msg($"[PartClipboard] 就绪：剪贴板 {library.Entries.Count} 项，文件 {path}");
        }

        public override void OnDeinitializeMelon()
        {
            copyAction?.Dispose();
            pasteAction?.Dispose();
            copyAction = null;
            pasteAction = null;
            service = null;
        }

        private void RegisterActions()
        {
            if (!SprocketApi.TryGetService<IInputService>(out IInputService? input) || input == null)
            {
                LoggerInstance.Error("[PartClipboard] SprocketModAPI 的输入服务不可用，Ctrl+C / Ctrl+V 未被接管。");
                return;
            }

            copyAction = input.RegisterAction(new ModActionDefinition
            {
                ActionId = CopyActionId,
                DisplayName = "Copy Part Subtree",
                Category = "Part Clipboard",
                Description = "Copy the selected part and its whole subtree into the clipboard.",
                // 修饰键按精确匹配，所以左右 Ctrl 各占一个槽位：`AnyCtrl` 会被解析成"左右都按住"。
                DefaultPrimary = new KeyChord("<Keyboard>/c", ModifierKeys.LeftCtrl),
                DefaultSecondary = new KeyChord("<Keyboard>/c", ModifierKeys.RightCtrl),
                Contexts = InputContextMask.Designer,
            });
            copyAction.Pressed += OnCopy;

            pasteAction = input.RegisterAction(new ModActionDefinition
            {
                ActionId = PasteActionId,
                DisplayName = "Paste Part Subtree",
                Category = "Part Clipboard",
                Description = "Paste the clipboard subtree into the current design and start drag placement.",
                DefaultPrimary = new KeyChord("<Keyboard>/v", ModifierKeys.LeftCtrl),
                DefaultSecondary = new KeyChord("<Keyboard>/v", ModifierKeys.RightCtrl),
                Contexts = InputContextMask.Designer,
            });
            pasteAction.Pressed += OnPaste;
        }

        private void OnCopy()
        {
            Run(service?.Copy());
        }

        private void OnPaste()
        {
            Run(service?.Paste());
        }

        private void Run(ClipboardAction? action)
        {
            if (action == null)
                return;

            lastDispatchFrame = Time.frameCount;

            if (action.Succeeded)
                LoggerInstance.Msg($"[PartClipboard] {action.Message}");
            else
                LoggerInstance.Warning($"[PartClipboard] {action.Message}");
        }

        // 按键系统没派发动作时给出可读线索：物理键确实按下了，问题在绑定还是上下文。
        public override void OnUpdate()
        {
            Designer.BuildPump.Tick();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.ctrlKey.isPressed)
                return;

            bool copy = keyboard.cKey.wasPressedThisFrame;
            bool paste = keyboard.vKey.wasPressedThisFrame;
            if (!copy && !paste)
                return;

            if (Time.frameCount - lastDispatchFrame <= 2)
                return;

            bool designer = DesignerPartAccess.TryOpen(out _);
            LoggerInstance.Warning(
                $"[PartClipboard] 收到 {(copy ? "Ctrl+C" : "Ctrl+V")}，但没有任何动作被派发；"
                + $"设计器会话{(designer ? "已识别" : "未识别，多半不在设计器场景")}。");
        }
    }
}
