# ARAttentionTrainer — AR 注意力引导系统

面向高功能孤独症（ASD）儿童的注意力引导 MR 应用：儿童佩戴 Meta Quest 3，
系统通过前置相机实时检测互动对象（教师/家长）人脸，用卡通 IP 精灵的
「引导 → 隐藏 → 重现」循环，帮助儿童建立"别人说话时看对方"的注意力习惯。

- **Unity**：6000.4.3f1（Universal 3D / URP）｜ **目标平台**：Meta Quest 3
- **关键依赖**：Meta XR SDK 207（含 MRUK）、Unity Sentis、YOLOv9（MIT 许可）
- **文档**：设计文档在 `docs/specs/`，实施计划在 `docs/plans/`

## 组员快速上手

1. `git clone <仓库地址>`（把本仓库拷到你自己电脑）
2. 安装 Unity Hub + Unity 6000.4.3f1（安装时勾选 Android Build Support）
3. Unity Hub → **Add** → **Add project from disk** → 选择克隆下来的文件夹
4. 每次开工前先 `git pull` 拿最新代码；改动完成后按流程提交推送

## 目录结构

| 目录 | 内容 |
|------|------|
| `Assets/Scripts/` | 代码（Core / FrameSource / Detection / UI …） |
| `Assets/Tests/` | 单元测试（EditMode） |
| `Assets/Models/` | 推理模型文件（YOLOv9） |
| `Assets/Scenes/` | 场景（Main / DevEditor） |
| `docs/` | 设计文档、实施计划、实测记录 |
| `tools/` | 开发辅助（数据分析脚本、离线包归档） |

## 构建到 Quest 3

（M1 里程碑完成后补充：CLI 构建命令与 BuildScript 用法）
