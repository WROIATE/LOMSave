# 活侠传自由存档 · 增强版

基于 [NothingNullNull/LOMSave](https://github.com/NothingNullNull/LOMSave) 扩展的《活侠传》便利功能 MOD，当前增强插件版本为 **0.19.3**，游戏加载时名称为 **LOMSave Probe**。

原项目作者和贡献者为 **NothingNullNull / What、black**；剧情前快照、记录选择并快速重播的自由存档思路来自原项目。增强版由 [WROIATE](https://github.com/WROIATE) 维护，保留原项目来源和提交历史。

本分支基于原项目提交 [`6d40806`](https://github.com/NothingNullNull/LOMSave/commit/6d408069b87d95809fe085f16928760958dfe4e7)。上游后来发布了采用新架构的 LOMSave 2.x；**下面的“原版”指这个旧版基线，不是与上游 2.x 的逐项比较**。本分支没有混入 2.x 的重写，也没有验证两者的存档附加文件能相互迁移。

## 功能与原版的区别

| 功能 | 原版基线 | 本增强版 |
| --- | --- | --- |
| 剧情内自由存读档 | 已有，通过剧情快照和快速重播恢复 | 扩展台词、普通／计时选项和骰子判定的进度记录；恢复背景、人物和声音，增加校验与超时回退 |
| 手动存档槽 | 原生数量 | 扩展为 50 个，增加可编辑备注 |
| 行动前自动存档 | 使用原生自动档 | 增加独立的“最近行动”档；唐门养成和外地共用行动菜单都能在行动前保存 |
| 自动播放 | 原生节奏 | 0.5～5 倍速度，可按文本长度调整，放在独立“对话设定”中 |
| 选项影响预览 | 未提供 | 长按查看本段属性、好感、物品、天赋等影响，可展开后续影响和关联事件 |
| 建筑事件预览 | 未提供 | 长按建筑查看当前人物事件、随机事件候选及条件，并可指定兼容事件池的下次事件 |
| 剧情判定提示 | 未提供 | 在回想中记录判定；可开启实时提示，逐条出现、依次淡出 |
| 编码名称 | 使用原始游戏说明 | 增加 YAML 名称表，支持修改后自动重新加载；无可靠名称时保留编号 |
| 混战快捷结束 | 未提供 | 增加快捷胜利按钮，调用游戏结算并模拟部分拾钱逻辑 |

预览属于剧情信息提示：静态分析不能覆盖所有动态 Lua 表达式，候选事件也不代表一定触发。条件、随机权重和后续影响会按可解析的数据展示。

## 安装

已经验证的游戏环境：`release_1.0.5000.13`、Unity `2020.3.49f1`、BepInEx 6 Mono。游戏或框架更新后需重新确认兼容性。

1. 安装与游戏架构匹配的 [BepInEx](https://github.com/BepInEx/BepInEx)，确保已有可用的 BepInEx 6 Mono 环境。
2. 下载本分支源码压缩包，打开 [`artifacts/LOMSaveProbe`](artifacts/LOMSaveProbe)。其中已包含编译产物。
3. 将以下文件放进游戏目录的 `BepInEx/plugins/`：
   - `LOMSaveProbe.dll`
   - `Newtonsoft.Json.dll`
   - `YamlDotNet.dll`
4. 将 `LOMSaveProbe.CodeNames.yaml` 放进 `BepInEx/config/`。名称表缺失时会回退到游戏原说明或编号。
5. 保留随包提供的两个第三方许可证文件，然后重启游戏。

同一套存档补丁应只安装一份。安装本增强插件时，请把原来的 `LOMSave.dll`、`LOMSave2.dll` 或旧版 `LOMSaveProbe.dll` 移到插件目录外，避免多份存读档补丁同时执行。仓库中的旧版 `LOMSavePlugins.cs` 保留为上游来源，本增强版编译入口是 `LOMSaveProbe.csproj`。

## 使用

### 存档、备注与“最近行动”

通过游戏原生保存／读取菜单操作；支持的剧情台词、选项和骰子判定界面可保存。手动档旁点击“添加备注”或已有备注即可编辑。

“最近行动”在执行消耗行动的操作之前更新，保存的是未扣行动次数、未执行该选项的状态。它是每次行动前保存，不是只在每旬开始保存。外地剧情菜单按共用选项的行动消耗字段判断，不按崆峒、青城等地图名写特例；免费离开、只消耗贡献、不可选项目和读档重放不会覆盖这个档。

剧情进度保存在原存档旁的 `<存档文件>.lomsaveprobe.json` 中，包含脚本、台词／选项检查点、已选分支和画面／声音状态。手动档、原生剧情自动档及“最近行动”分别读取自己的附加记录，并校验保存时间、文件哈希与脚本。

复制或备份存档时，应一起保留 `.dat`、`.lomsaveprobe.json` 和可选的 `.note.txt`。没有匹配附加记录的旧档按游戏原生流程读取，不能凭空恢复过去的精确台词位置。重放失败时会取消恢复或回退到剧情起点；过场、复杂循环和其他 MOD 改写的脚本不能保证都能精确还原。

### 长按预览

- 长按选项约 **0.5 秒**打开影响预览；按住 **0.2 秒**后才显示进度反馈。
- 长按养成地图的建筑查看人物事件和随机候选；普通点击仍执行游戏原操作。
- “查看后续影响”“查看事件”用于展开更多信息。
- “设为下次事件”只对界面标记为可指定的兼容事件池生效，可取消。
- **右键关闭当前预览窗口**，不会同时打开游戏设置。

### 对话设定与实时判定

游戏设置页的“对话设定”提供自动播放速度、“按文本长度调整”和实时剧情判定提示。

速度数值越大，等待越短。关闭按长度调整时，等待按固定基准 `8 秒 ÷ 速度` 计算；开启时，根据显示文本的字符数、标点停顿和起始缓冲计算，再除以速度。当前基准为起始缓冲 0.5 秒、每个可见字符 0.20 秒，基准等待限制在 1.2～15 秒，最终仍会等待文字显示完成。

实时判定提示默认关闭。开启后，条件及通过／未通过结果会以独立消息逐条展示；回想也会记录新触发的判定。它们不改写人物台词，也不重新执行随机判定。

### 名称映射

编辑 `BepInEx/config/LOMSaveProbe.CodeNames.yaml` 中的 `name` 并保存，插件约每秒检查并重新加载。当前表统计 4,825 个编码，其中 4,335 个有非空名称；部分名称依据上下文归纳，不是官方 DevNote。

分类包括 `flags`、`conditions`、`switches`、`events`、`missions`、`mission_groups`。空名称或未收录编号回退原生说明／编号；格式错误时继续使用上一份有效映射。已写入的旧回想不会重写，已打开的预览需重新打开。

## 编译与检查

需要游戏本地的 `Mortal_Data/Managed`、BepInEx 的 `core` 引用及发布目录中的第三方依赖；仓库不包含游戏程序集。已使用 PowerShell 7 的 Roslyn 编译器构建，不需要另装终端 .NET SDK。

```powershell
pwsh -File ./Build-Local.ps1 -GameDir '你的 LegendOfMortal 游戏目录'
```

产物写入 `artifacts/LOMSaveProbe/LOMSaveProbe.dll`；可用 `-OutputPath` 指定其他位置。构建后需手动替换插件并重启游戏。

六组检查各自运行在独立 PowerShell 进程中，避免测试替身的类型名互相影响：

```powershell
pwsh -File ./tests/Test-ActionAutoSave.ps1
pwsh -File ./tests/Test-StorySaveFiles.ps1
pwsh -File ./tests/Test-ReplayRuntime.ps1
pwsh -File ./tests/Test-DialogueAndImpact.ps1
pwsh -File ./tests/Test-BuildingLongPress.ps1
pwsh -File ./tests/Test-CodeNames.ps1
```

这些检查覆盖保存时序、恢复生命周期、文本计时、预览输入和 YAML 加载，不能代替实际游戏内验证。

`tools/` 提供从本地游戏数据导出名称、归纳未命名条件的工具。Python 依赖为 UnityPy；可用 `python -m pip install -r tools/requirements.txt` 安装，然后运行 `tools/Update-CodeNameContexts.ps1 -GameDir '游戏目录' -Python 'python解释器路径'`。生成的详细溯源与分析报告仅用于本地开发，游戏只需要主名称表。

诊断日志位于 `BepInEx/LOMSaveProbe/`；`F10` 可追加状态快照。日志可能包含存档状态和台词，分享前请检查内容。

## 致谢与来源

- **[NothingNullNull/LOMSave](https://github.com/NothingNullNull/LOMSave)**：原项目及其作者、贡献者，提供自由存档和快速重播的基础思路。
- **[YaN9Yu96/MortalControlMyDestiny（知天命）](https://github.com/YaN9Yu96/MortalControlMyDestiny)**：参考 DevNote 名称解析和调用游戏原生浮动消息的方式。
- **[活侠传助手](https://mod.3dmgame.com/mod/256420)**：参考建筑事件查看、分支检查点和指定下次事件的交互与数据挂接思路。
- **[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)**、**[YamlDotNet](https://github.com/aaubry/YamlDotNet)**：第三方依赖；各自 MIT 许可证随发布文件保留。

当前基线的原仓库没有独立 LICENSE 文件，本 fork 没有为原作者代码另行声明新的许可证。游戏资源、个人存档、本地备份及无关设计文档不随本仓库提交。
