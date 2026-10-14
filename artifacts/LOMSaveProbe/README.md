# 活侠传自由存档增强版 0.19.3

基于 [NothingNullNull/LOMSave](https://github.com/NothingNullNull/LOMSave) 的旧版 `6d40806`，感谢原作者 NothingNullNull / What、black。增强版由 [WROIATE](https://github.com/WROIATE) 维护；完整功能、原版差异与编译说明见[仓库 README](../../README.md)。

## 安装文件

使用 BepInEx 6 Mono。游戏验证环境为 `release_1.0.5000.13`、Unity `2020.3.49f1`。

| 文件 | 位置 |
| --- | --- |
| `LOMSaveProbe.dll` | 游戏目录 `BepInEx/plugins/` |
| `Newtonsoft.Json.dll` | 游戏目录 `BepInEx/plugins/` |
| `YamlDotNet.dll` | 游戏目录 `BepInEx/plugins/` |
| `LOMSaveProbe.CodeNames.yaml` | 游戏目录 `BepInEx/config/` |
| `Newtonsoft.Json.LICENSE.txt`、`YamlDotNet.LICENSE.txt` | 保留第三方许可证 |

把原来的 `LOMSave.dll`、`LOMSave2.dll` 或旧版增强插件移到插件目录外，只启用一份存档插件，然后重启游戏。

## 使用要点

- 游戏原生保存／读取菜单支持剧情内存档，手动档槽扩展为 50 个，档旁可编辑备注。
- “最近行动”在唐门养成和兼容的外地行动菜单中执行耗行动操作前更新，读档可回到行动前。
- 长按选项或养成建筑约 0.5 秒查看影响／事件；0.2 秒后显示进度，右键关闭预览。
- 设置页“对话设定”提供自动播放速度、按文本长度调整和实时剧情判定提示。
- 剧情判定会写入新回想；实时提示可选开启，消息逐条显示并依次淡出。
- 主名称表可编辑并自动重新加载；无说明的编码保留编号。
- 混战支持快捷胜利按钮。

精确位置记录是原存档旁的 `.lomsaveprobe.json`，备注是 `.note.txt`；备份时一起保留。缺少或不匹配位置记录的旧档按原生流程读取，无法补回原来的精确台词位置。静态预览和复杂剧情的恢复仍有范围限制，详见仓库说明。
