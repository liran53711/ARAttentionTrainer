#!/usr/bin/env bash
# 从 Quest 3 拉取训练会话数据到本机（默认落到仓库的 data/ 目录，已被 .gitignore 忽略）
# 用法：bash tools/pull_sessions.sh [目标目录]
# 前置：USB 连接 Quest 3 且授权调试（adb devices 可见设备）
set -e

ADB="${ADB:-D:/Development/Unity/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
PKG="com.UnityTechnologies.com.unity.template.urpblank"
REMOTE="/sdcard/Android/data/$PKG/files/sessions"
HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="${1:-$HERE/../data}"

mkdir -p "$DEST"

if ! "$ADB" devices | grep -qE "device$"; then
  echo "⚠️ 未检测到已授权设备——请用 USB 连接 Quest 3 并在头显里允许调试。"
  "$ADB" devices
  exit 1
fi

echo "→ 拉取 $REMOTE  →  $DEST/sessions"
"$ADB" pull "$REMOTE" "$DEST/"
echo "✅ 完成。分析：python tools/analyze_sessions.py \"$DEST/sessions\""
