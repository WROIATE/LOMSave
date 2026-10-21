# 活侠传自由存档 · 增强版

基于 [LOMSave](https://github.com/NothingNullNull/LOMSave) 扩展的《活侠传》便利功能 MOD，游戏内插件名称为 LOMSave Probe。

## 功能简介

- 自由存读档：原版允许存档的位置沿用原生存读档；额外开放的剧情自由存档须在文字完整显示、演出结束的对白处保存，读档直接续接；支持 50 个手动槽位及存档备注。
- 行动自动存档：执行行动前保存“最近行动”，可返回行动前重新安排。
- 分支快速返回：选择、限时选项及投骰前保存“最近分支”，按 F8 返回重新选择。
- 死亡页读档：右下角直接打开读档列表，可选择手动档及自动档。
- 选项与事件预览：长按或中键查看条件、属性、好感及奖励影响，支持展开后续影响。
- 事件指定：按行动页签查看，待触发剧情单列；支持单次指定和固定选择，默认隐藏不能触发的事件，固定偏好跨读档和重启保留。
- 对话与判定：支持 0.5～5 倍自动播放、按文本长度调整等待，以及剧情判定提示和回想记录。
- 混战直接胜利：准备页和暂停页均可操作，按原生规则结算金钱、属性与点数奖励。
- 四师兄弹珠跳过：可选 0／30／60／100 分，奖励由原生剧情发放。
- 剧情日历：日期旁仅提示当前可做且未读的事件，查看后不再提醒；已读状态、行动与事件观察记录跟随存档，读回查看前的存档会恢复提示，新周目重新记录；F10 汇总当前剧情，全年日期表支持历史追溯和跨周目手动备注。
- 回想快捷操作：剧情中鼠标滚轮向上打开回想。
- 调试面板增强：剧情或养成界面按 F9 打开，保留加减按钮，补全变心及养成点数，可分别调整修炼、炼丹、锻造每回合点数上限并恢复原生值，设置跨读档和重启保留；旗标显示中文、悬停查看代码；F9 或 Esc 关闭，无需另装修改器。
- 读档声音恢复：恢复背景音乐与环境音的音量、渐变，避免错误播放雨声等环境音。

## 安装方法

当前实测游戏版本为 `release_1.0.6000.5`，运行环境为 Windows x64、Unity 2020.3.49f1、BepInEx 6 Unity.Mono。

1. 退出游戏并备份存档；安装可用的 **[BepInEx 6 Unity.Mono Windows x64](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_mono.html)**，安装包不包含该框架。
2. 从 [Releases](https://github.com/WROIATE/LOMSave/releases) 下载安装 ZIP，将其中的 `BepInEx` 文件夹合并到游戏根目录（`Mortal.exe` 所在目录），保留包内许可证。
3. 将旧的 `LOMSave.dll`、`LOMSave2.dll` 或重复的增强插件移到插件目录外，只启用一份存档插件。
4. 保留插件旁的 `LOMSaveProbe.StoryEvents.json`，用于冷启动时显示特殊事件提示。启动游戏，通过原生存读档菜单及设置页使用功能；安装包已内置 F9 面板入口。已安装 ScriptTrainer 时沿用其快捷键，避免重复触发。

也可从[插件文件目录](https://github.com/WROIATE/LOMSave/tree/enhanced/artifacts/LOMSaveProbe)手动安装：

| 文件 | 放置位置 |
| --- | --- |
| `LOMSaveProbe.dll`、`LOMSaveProbe.StoryEvents.json`、`Newtonsoft.Json.dll`、`YamlDotNet.dll` | 游戏目录 `BepInEx/plugins/` |
| `LOMSaveProbe.CodeNames.yaml` | 游戏目录 `BepInEx/config/` |

备份或迁移存档时，一并保留 `.dat` 旁的 `.lomsaveprobe.json`、`.lomsavecalendar.json` 和 `.note.txt`，其中 `.lomsavecalendar.json` 包含行动、事件已读与观察记录。手动日历备注、固定事件偏好、养成回合上限位于 `BepInEx/config/`，跨周目保留。旧档没有事件快照时从未读开始，历史只记录实际观察到的日期。旧自由档首次读取仍采用兼容恢复，在稳定对白重新保存后才使用直接续接。

## 免责声明

本项目仅用于学习与技术研究，不保证稳定性或与所有游戏版本、其他 MOD 的兼容性。使用可能造成卡顿、崩溃、存档损坏或进度丢失，请先备份并自行评估、承担使用风险。剧情预览可能包含剧透，静态分析结果不保证覆盖所有条件或一定触发事件。

## 致谢与来源

- [NothingNullNull/LOMSave](https://github.com/NothingNullNull/LOMSave)：原作者及贡献者提供自由存档基础；本增强版基于旧版提交 [6d40806](https://github.com/NothingNullNull/LOMSave/commit/6d408069b87d95809fe085f16928760958dfe4e7)，与上游 2.x 分开维护。
- [YaN9Yu96/MortalControlMyDestiny（知天命）](https://github.com/YaN9Yu96/MortalControlMyDestiny)：参考名称解析与原生浮动消息的使用方式。
- [活侠传助手](https://mod.3dmgame.com/mod/256420)：参考建筑事件查看、分支检查点和事件指定思路。
- [Jim97/UnityScriptTrainer](https://github.com/jim972329667/UnityScriptTrainer/tree/master/Mortal)：原先提供游戏测试面板入口。本包的 F9 开关为独立实现，不分发该插件。
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)、[YamlDotNet](https://github.com/aaubry/YamlDotNet)：第三方依赖，各自 MIT 许可证随插件保留。
