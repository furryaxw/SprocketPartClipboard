using System;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(SprocketPartClipboard.PartClipboardMod), "Part Clipboard", "0.1.0", "furryAxw")]
[assembly: MelonGame("HD", "Sprocket")]
[assembly: MelonAdditionalDependencies("SprocketModAPI")]
[assembly: AssemblyMetadata("Sprocket.Mod.Id", "furryaxw.sprocket-part-clipboard")]
[assembly: AssemblyMetadata("Sprocket.Mod.DisplayName", "Part Clipboard")]
[assembly: AssemblyMetadata("Sprocket.Mod.Description", "Copies the selected part subtree in the vehicle designer and pastes it back as a draggable assembly.")]
[assembly: AssemblyMetadata("Sprocket.Mod.Authors", "furryAxw")]
[assembly: AssemblyMetadata("Sprocket.Mod.Repository", "furryAxw/SprocketPartClipboard")]
[assembly: AssemblyMetadata("Sprocket.Mod.Category", "designer")]
[assembly: AssemblyMetadata("Sprocket.Mod.License", "GPL-3.0-only")]
