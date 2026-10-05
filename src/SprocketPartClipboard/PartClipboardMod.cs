using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SprocketModAPI;
using SprocketPartClipboard.Clipboard;
using SprocketPartClipboard.Designer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SprocketPartClipboard
{
    [BepInPlugin(PluginGuid, "Part Clipboard", "0.1.1")]
    [BepInDependency("furryaxw.sprocket-mod-api")]
    public sealed class PartClipboardMod : BasePlugin
    {
        internal const string PluginGuid = "furryaxw.sprocket-part-clipboard";

        private const string CopyActionId = "copy-part-tree";
        private const string PasteActionId = "paste-part-tree";

        private ClipboardLibrary library = new ClipboardLibrary();
        private PartTreeClipboardService? service;
        private IInputActionHandle? copyAction;
        private IInputActionHandle? pasteAction;
        private int lastDispatchFrame = -1;

        public override void Load()
        {
            // BepInEx 没有 OnUpdate：注入组件承担每帧回调。
            AddComponent<PartClipboardUpdater>().Configure(this);
            Harmony.CreateAndPatchAll(typeof(PartClipboardMod).Assembly, PluginGuid);

            string path = ClipboardLibraryStore.DefaultFilePath(UserDataDirectory());
            ClipboardLibraryStore store = new ClipboardLibraryStore(path);
            ClipboardLoadResult loaded = store.Load();

            library = loaded.Library;
            service = new PartTreeClipboardService(store, library);

            if (loaded.RecoveredFromCorruption)
                Log.LogWarning($"[PartClipboard] 剪贴板文件无法解析，已保留为 {loaded.BackupPath}，本次从空剪贴板开始。");
            else if (loaded.Error != null)
                Log.LogWarning($"[PartClipboard] 剪贴板文件读取失败：{loaded.Error}");

            RegisterActions();
            Log.LogInfo($"[PartClipboard] 就绪：剪贴板 {library.Entries.Count} 项，文件 {path}");
        }

        public override bool Unload()
        {
            copyAction?.Dispose();
            pasteAction?.Dispose();
            copyAction = null;
            pasteAction = null;
            service = null;
            return true;
        }

        // MelonLoader 的 UserData 目录在 BepInEx 下没有对应项：沿用游戏根目录下的同名目录，
        // 剪贴板文件位置与文档保持一致。
        private static string UserDataDirectory()
        {
            return Path.Combine(Paths.GameRootPath, "UserData");
        }

        private void RegisterActions()
        {
            if (!SprocketApi.TryGetService<IInputService>(out IInputService? input) || input == null)
            {
                Log.LogError("[PartClipboard] SprocketModAPI 的输入服务不可用，Ctrl+C / Ctrl+V 未被接管。");
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
                Log.LogInfo($"[PartClipboard] {action.Message}");
            else
                Log.LogWarning($"[PartClipboard] {action.Message}");
        }

        // 按键系统没派发动作时给出可读线索：物理键确实按下了，问题在绑定还是上下文。
        internal void Tick()
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
            Log.LogWarning(
                $"[PartClipboard] 收到 {(copy ? "Ctrl+C" : "Ctrl+V")}，但没有任何动作被派发；"
                + $"设计器会话{(designer ? "已识别" : "未识别，多半不在设计器场景")}。");
        }
    }

    // BepInEx 没有每帧回调；这个注入组件把 Update 转给宿主插件。
    internal sealed class PartClipboardUpdater : MonoBehaviour
    {
        private PartClipboardMod? host;

        public PartClipboardUpdater(IntPtr ptr) : base(ptr)
        {
        }

        public void Configure(PartClipboardMod mod) => host = mod;

        private void Update() => host?.Tick();
    }
}
