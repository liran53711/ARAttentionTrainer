# 人工操作清单（2026-10-10 用户回来后执行）

> 本清单由 Claude 自主开发块结束时整理。所有"需要动手"的事项集中在此，按顺序做即可。

## ① Git 提交（1 笔）

本清单所在的 `docs/` 也会一起进这笔提交。

```bash
cd /d/Development/Unity/ARAttentionTrainer
git add -A
git commit -m "feat: 世界空间IP精灵 + 三档干预参数(手柄切档) + 麦克风降级 + 数据链路增强（44测试全绿）" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
git push
```

## ② 部署（USB 连接 Quest 3 后）

```bash
cd /d/Development/Unity/ARAttentionTrainer
ADB=/d/Development/Unity/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe
$ADB devices                        # 确认识别到设备
$ADB install -r -g Builds/ARAttention-device.apk
$ADB shell pm grant com.UnityTechnologies.com.unity.template.urpblank android.permission.RECORD_AUDIO
```

## ③ 真机测试清单（戴上头显逐一验证）

**A. 世界空间 IP（新功能，重点）**
- [ ] 站到视野中 → 小精灵出现在头部附近（紫色圆脸占位），随人移动而移动
- [ ] 移出/遮挡摄像头 → 精灵停在原地不漂移（世界锚定）；重新出现 → 回到头部
- [ ] 注视精灵 0.8s → 精灵渐隐"躲起来"（状态 Guiding→Hidden）
- [ ] 讲话结束后仍在注视 → 精灵闪现金色脉冲（反馈）
- [ ] 长时间讲话中 → 精灵每 ~12 秒从头部侧面探头一次（Peek）

**B. 三档干预参数（手柄 A/X 键循环切换）**
- [ ] 按 A 键 → 面板"档位"变化：中→弱→强→中
- [ ] 强档（大号精灵、音效更响、gazeStart 0.5s 更容易触发隐藏）
- [ ] 弱档（小号精灵、音效极轻、gazeStart 1.2s 要求更扎实的注视）
- [ ] 面板显示：`状态 X | 档位 强/中/弱 | 注视 X.Xs`

**C. 麦克风降级模式（可选，验证应急路径）**
- [ ] 撤销麦克风权限再启动：
  `adb shell pm revoke com.UnityTechnologies.com.unity.template.urpblank android.permission.RECORD_AUDIO`
- [ ] 日志/表现：始终引导（不再等"讲话"）；注视累计满 5s 直接给反馈
- [ ] 测完恢复：`adb shell pm grant ... RECORD_AUDIO`

**D. 数据链路**
- [ ] 玩 1-2 分钟后退出应用
- [ ] `bash tools/pull_sessions.sh` 拉取到 `data/sessions/`
- [ ] `python tools/analyze_sessions.py data/sessions` → 打开 `data/analysis/summary.md` 看汇总 + `plots/` 出图

## ④ 已知事项

- **头显时钟错误**（显示 7 月）：会话目录日期命名会错——测试前让头显联网校时（设置→系统→关于，联网后自动同步）
- 电脑端回归（可选）：Unity 打开跑 DevEditor 场景，键盘 1/2/3 切档同样生效
- 若测试中发现数值不合意（如精灵太大/太小、Peek 太频繁），全部参数在 `Assets/Settings/ExperienceSettings.asset` 及档位表 `ApplyLevel` 里集中可调
