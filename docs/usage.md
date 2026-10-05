# 用法

> **这是一个非常危险的模组。** 它只在炮塔上做过完整测试；复制别的东西可能把载具改坏。
> 动手之前先备份存档，见 [README](../README.md)。

## 安装

1. 确认 `Mods\SprocketModAPI.dll` 存在（本模组依赖它的按键服务）。
2. 把 `SprocketPartClipboard.dll` 放进游戏根目录的 `Mods\`。
3. 启动游戏，日志里应出现 `[PartClipboard] 就绪：剪贴板 N 项，文件 …`。

## 复制

1. 进载具设计器，点选要复制的部件（例如炮塔环）。
2. 按 `Ctrl+C`。
3. 日志出现 `已复制 <部件名>（N 个部件，载荷 M 字符，载荷回读一致）`。

复制的是"该部件 + 它下面全部子件"，相对位置、镜像、缩放都跟着走。

## 粘贴

1. 进任意载具的设计器（可以是另一辆车，也可以重启游戏之后再进）。
2. 想让子树挂到哪个部件下面，就先点选那个部件；不想挂到某个部件下就直接按 `Ctrl+V`，它会挂到载具根。
3. 按 `Ctrl+V`。
4. 日志出现一条：

   ```
   已粘贴 X：新建 N 个部件（其中 K 个作为树根挂到 <锚点>），注册蓝图定义 M 条（计入使用 U 条）+ 网格 G 条，重建 R 个组件，补炮座耳轴 T 处，手持 Reattach 已开启。
   ```

   同车与跨载具走的是同一条路径。粘贴**不进撤销栈**。

5. 摆放：粘贴后副本会进入手持状态（走游戏自己的"拿起部件"流程 `Reattach`：先拆卸、再放置），
   整棵树跟着鼠标走，点一下落到想要的位置。

`Ctrl+V` 粘贴的是剪贴板里**最近一次复制**的条目。

## 键位

两个动作注册在 SprocketModAPI 的按键系统里，动作 ID 是 `copy-part-tree` 与 `paste-part-tree`，默认绑定：

- `copy-part-tree` → `Left Ctrl` / `Right Ctrl` + `C`
- `paste-part-tree` → `Left Ctrl` / `Right Ctrl` + `V`

游戏内「设置 → Keymapping」页面左下角有 `MOD KEYBINDINGS` 入口，可以在那里改键或解绑。游戏自带的复制键位（原生 duplicate）是 `Alt`，与本模组不冲突。

## 剪贴板文件

- 路径：`BepInEx\config\SprocketPartClipboard\library.json`
- 结构：`SchemaVersion` / `ActiveId` / `Entries[]`，每个条目含 `Id`、`Name`、`CapturedUtc`、`GameVersion`、`SourceVehicle`、`RootPartId`、`PartCount`、`ComponentVuids`、`Payload`。
- `Payload` 是游戏自己的载具蓝图 JSON 文本，本模组不解释它的内容。
- `ComponentVuids` 是复制时记下的这棵子树里全部组件的 VUID，粘贴时用它精确偏移组件编号（组件编号在载荷里以组件标识为键存放，且不一定紧挨着所属部件编号）。早于该字段的条目没有它，粘贴侧退回"部件编号之后的窄带"判定。
- 写入是原子的：先写 `library.json.tmp` 再整体替换，不会留下半截文件。
- 读不动的文件会被改名为 `library.corrupt-<时间戳>.json` 保留，然后从空剪贴板开始。
- 文件里不认识的键会被原样保留。
- 这个文件是本模组自己的，删掉它只清空剪贴板，不影响游戏存档。

## 排错

日志标签是 `[PartClipboard]`，在 `MelonLoader\Latest.log` 里搜它即可。

| 日志 | 含义 |
| --- | --- |
| `收到 Ctrl+C/Ctrl+V，但没有任何动作被派发；设计器会话已识别/未识别。` | 物理按键到了、但按键系统没派发动作。`未识别` 说明当前场景没被判定成设计器；`已识别` 说明是绑定没有命中。 |
| `当前不在载具设计器里。` | 按键时不在设计器场景；动作只在 Designer 上下文派发。 |
| `没有选中部件，先在设计器里点选一个部件。` | `Ctrl+C` 时没有活动选中项。 |
| `剪贴板是空的，先 Ctrl+C 复制一棵子树。` | `Ctrl+V` 之前没有复制过任何东西。 |
| `读不回剪贴板里的“X”，它的数据可能来自另一个游戏版本。` | 剪贴板条目由别的游戏版本写出。 |
| `导出子树失败：游戏返回了空的部件列表。` | 选中的部件没有可导出的子树。 |
| `拿不到当前载具。` / `拿不到当前载具的部件注册表。` | 设计器已打开但目标载具尚未就绪，换个时机再按一次。 |
| `Item 'X' cannot be registered … already contains a reference … using VUID 'N'` | 副本的某个编号与目标载具里既有部件撞号。把这条连同日志一起反馈。 |
| `构建泵结束：X 轮，末次脏标志 …` | 粘贴后的跨帧构建收敛情况；末次为 `None` 表示已收敛。 |
| `Plugin VehicleEditorGizmoDrawer threw an exception during late update.` | 游戏编辑器插件画 gizmo 时被空耳轴打断；本模组会在粘贴后补齐该依赖，若仍出现请反馈。 |
