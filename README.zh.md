# Sprocket Part Clipboard

**中文** | [English](README.md)

在《Sprocket》载具设计器里复制与粘贴整棵部件子树。

> ## ⚠️ 这是一个非常危险的模组
>
> - **只在炮塔上做过完整测试**：本项目全部游戏内验证都是围绕炮塔环（`turretRing`）那棵树做的。
>   复制别的东西**没有得到过同等验证**，可能把载具改坏、也可能写出坏档案。
> - **复制其它东西之前请先备份存档**。载具设计一旦被写坏，游戏通常没有撤销可用（本模组粘贴也不进撤销栈）。
> - 剪贴板文件在 `BepInEx\config\SprocketPartClipboard\library.json`；它是本模组自己的文件，
>   删掉它只会清空剪贴板，不影响游戏存档。
>
> 已知问题与成因见下面的「已知问题」一节的说明。

## 功能

- `Ctrl+C`：把设计器里选中的部件、连同它下面整棵子树抓进剪贴板。
- `Ctrl+V`：把剪贴板里的子树还原到当前载具，挂到选中部件下，并进入拖动摆放。
- 剪贴板跨载具、跨启动保留：`BepInEx\config\SprocketPartClipboard\library.json` 最多保存 64 项，`Ctrl+V` 粘贴最近一次复制的那项。
- 键位由 SprocketModAPI 管理，可在游戏内「设置 → Keymapping → MOD KEYBINDINGS」里改。

## 安装

1. 你已安装 BepInEx 6（IL2CPP），且 `BepInEx\plugins` 里已有 `SprocketModAPI.dll`（1.0.0 或更高）。
2. 把 `SprocketPartClipboard.dll` 放进 `BepInEx\plugins`。

## 已知问题

- 黏贴炮塔后弹药架在模拟时被渲染成 1×1×1 的方块。可用的修复：粘贴之后保存并重新读取恢复。（该问题不影响战斗）
- 报错信息（`BepInEx\LogOutput.log`）里若出现
  `Plugin VehicleEditorGizmoDrawer threw an exception`，那是游戏编辑器插件在画 gizmo 时被
  空耳轴打断，本模组已尽量在粘贴后补齐该依赖。

## 当前限制

- 粘贴不进撤销栈（`Instantiate` 不产生 operation）。
- 多选时只复制活动部件（最后点中的那个）那一棵子树。
- 剪贴板没有浏览界面，`Ctrl+V` 总是粘贴最近一次复制的条目。

## 实现要点

- 抓取走 `VehicleObjectSerialization.ToBlueprints`（上下文 `SpawnStateChange`），落盘走游戏自带的
  `VehicleBlueprintSerializer`：剪贴板载荷就是游戏自己的载具蓝图 JSON，只是 `objects` 里只装被复制的子树。
- 粘贴只有一条路径（同车与跨载具共用）：把载荷里的部件/组件/蓝图/网格四类编号按目标载具的号段改指 →
  把 `blueprints[]` / `meshes[]` 注册进目标载具（只被蓝图内部整数引用的定义额外计入一次使用）→
  `InstantiateStructured`（`FullSave` 上下文，编号由 `LoadVuids` 装回）→ 组件立脏位并跨帧构建到收敛 →
  补炮座耳轴 → `Reattach` 进入手持摆放。
- 游戏自带的 `VehicleSubAssemblySerialization` 在 0.2.55.5 里是两个未实现的桩，本项目不使用它。

细节与逆向证据见 [docs/status.md](docs/status.md)，用法见 [docs/usage.md](docs/usage.md)。

## 构建与测试

在仓库根目录运行：

```powershell
dotnet build .\src\SprocketPartClipboard\SprocketPartClipboard.csproj --configuration Release
dotnet run --configuration Release --project .\tests\SprocketPartClipboard.ContractTests\SprocketPartClipboard.ContractTests.csproj
powershell -File .\tools\verify-package.ps1
```

默认构建会部署到 `$(SprocketGameRoot)\BepInEx\plugins`；加 `-p:SkipModDeploy=true` 只构建。
