# 建模与贴图资源清单

> 更新：2026-10-16

## 建模

| 文件 | 说明 |
|---|---|
| `Assets/Art/IP/CH_Sprite.fbx` | **精灵 3D 模型**（组员制作）：约 7.7k 三角面、8 个动画、5 个表情形态键。导入设置：`bakeAxisConversion=false`、朝向 +Z。配套动画控制器为同目录 `CH_Sprite.controller` |

## 贴图（`Assets/Art/IP/`）

| 文件 | 说明 |
|---|---|
| `Sprite_Atlas_1024.png` | **CH_Sprite 模型贴图集**（1024×1024），精灵身体/脸部配色 |
| `glow_soft.png` | 特效·柔光晕（精灵背后呼吸光） |
| `star_soft.png` | 特效·柔星（星尘/萤火虫/粒子） |
| `ring_soft.png` | 特效·柔环（锁定/达成/庆典的扩散光环） |
| `bubble.png` | 情绪泡泡·气泡框底 |
| `face_cheer.png` | 情绪泡泡·欢呼表情 |
| `face_hum.png` | 情绪泡泡·哼歌表情 |
| `note_sing.png` | 情绪泡泡·浮动音符 |
| `ArrowRibbon.png` | 引导飘带（1024×256）——**已停用**（改为立体声方位引导，存档备用） |

贴图设计源在 `docs/design/*.html`：改样式改 HTML → 透明背景截图导出 PNG → 覆盖同名文件。
