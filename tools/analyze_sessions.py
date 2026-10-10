# -*- coding: utf-8 -*-
"""训练会话数据分析：单会话概览 + 多会话汇总 + 出图。

用法：
  python tools/analyze_sessions.py <sessions目录> [--out <输出目录>] [--no-plot] [--selftest]

输入：SessionLogger 在设备上写出的会话目录，每个会话含——
  session.json  元数据（含 settings 参数快照）
  events.jsonl  事件流：SessionStart / StateChange / GuideStart / GazeStart /
                GazeSustained / Feedback / SpeechEnd / TargetAcquired / TargetLost /
                LevelChange / MicFallback / SessionEnd
  samples.csv   周期采样：t,looking,speaking,targetValid,fps,inferMs

输出（默认 <sessions目录>/../analysis/）：
  summary.md / summary.csv   汇总
  plots/<会话>.png           每会话图（需 matplotlib；缺省时跳过并提示）
"""
import argparse
import csv
import json
import os
import statistics
import sys


# ---------- 读取 ----------

def find_sessions(root):
    out = []
    for name in sorted(os.listdir(root)):
        d = os.path.join(root, name)
        if os.path.isdir(d) and os.path.exists(os.path.join(d, "session.json")):
            out.append(d)
    return out


def read_json(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def read_events(path):
    events = []
    if not os.path.exists(path):
        return events
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except Exception:
                pass
    return events


def read_samples(path):
    rows = []
    if not os.path.exists(path):
        return rows
    with open(path, encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            try:
                rows.append({
                    "t": float(row["t"]),
                    "looking": int(row["looking"]),
                    "speaking": int(row["speaking"]),
                    "targetValid": int(row["targetValid"]),
                    "fps": float(row["fps"]),
                    "inferMs": float(row["inferMs"]),
                })
            except Exception:
                pass
    return rows


# ---------- 统计 ----------

def analyze_session(d):
    meta = read_json(os.path.join(d, "session.json"))
    events = read_events(os.path.join(d, "events.jsonl"))
    samples = read_samples(os.path.join(d, "samples.csv"))

    n = len(samples)
    dur = samples[-1]["t"] if n else 0.0

    def pct(k):
        return (100.0 * sum(r[k] for r in samples) / n) if n else 0.0

    infer = [r["inferMs"] for r in samples if r["inferMs"] > 0]
    fps = [r["fps"] for r in samples if r["fps"] > 0]

    # 事件统计
    counts = {}
    for e in events:
        counts[e.get("type", "?")] = counts.get(e.get("type", "?"), 0) + 1

    # 各状态累计时长（StateChange 事件间隔）
    states = {"Idle": 0.0, "Guiding": 0.0, "Hidden": 0.0, "Feedback": 0.0}
    last_t, last_state = 0.0, "Idle"
    for e in events:
        if e.get("type") == "StateChange":
            det = str(e.get("detail", ""))
            if "->" in det:
                cur = det.split("->")[-1].strip()
            else:
                cur = det.strip()
            states[last_state] = states.get(last_state, 0.0) + (e.get("t", last_t) - last_t)
            last_t, last_state = e.get("t", last_t), cur
        elif e.get("type") == "SessionEnd":
            states[last_state] = states.get(last_state, 0.0) + (e.get("t", last_t) - last_t)
            last_t = e.get("t", last_t)

    return {
        "name": os.path.basename(d),
        "duration": dur,
        "n_samples": n,
        "looking_pct": pct("looking"),
        "speaking_pct": pct("speaking"),
        "target_pct": pct("targetValid"),
        "fps_avg": statistics.mean(fps) if fps else 0.0,
        "infer_avg": statistics.mean(infer) if infer else 0.0,
        "infer_p95": (statistics.quantiles(infer, n=20)[18] if len(infer) >= 20 else
                      (max(infer) if infer else 0.0)),
        "event_counts": counts,
        "state_seconds": states,
        "meta": meta,
        "samples": samples,
        "events": events,
    }


def fmt_row(s):
    c = s["event_counts"]
    return ("| {name} | {dur:.0f}s | {look:.0f}% | {spk:.0f}% | {tgt:.0f}% | {fps:.0f} | {inf:.0f}ms"
            " | 引导{c_guide} 反馈{c_fb} 注视达成{c_gs} |").format(
        name=s["name"], dur=s["duration"], look=s["looking_pct"], spk=s["speaking_pct"],
        tgt=s["target_pct"], fps=s["fps_avg"], inf=s["infer_avg"],
        c_guide=c.get("GuideStart", 0), c_fb=c.get("Feedback", 0), c_gs=c.get("GazeSustained", 0))


# ---------- 出图 ----------

def plot_session(s, outdir):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        plt.rcParams["font.sans-serif"] = ["Microsoft YaHei", "SimHei", "Arial Unicode MS"]
        plt.rcParams["axes.unicode_minus"] = False
    except Exception as e:
        return "（未装 matplotlib，跳过出图：%s）" % e

    samples = s["samples"]
    if not samples:
        return "（无采样数据，跳过）"

    ts = [r["t"] for r in samples]
    fig, ax = plt.subplots(3, 1, figsize=(10, 8), sharex=True)

    # 1) 行为曲线（阶梯）
    ax[0].step(ts, [r["looking"] for r in samples], where="post", label="注视")
    ax[0].step(ts, [r["speaking"] for r in samples], where="post", label="讲话", alpha=0.7)
    ax[0].step(ts, [r["targetValid"] for r in samples], where="post", label="目标有效", alpha=0.5)
    ax[0].set_ylim(-0.1, 1.1)
    ax[0].legend(loc="upper right", ncol=3)
    ax[0].set_ylabel("行为信号")
    ax[0].set_title("会话 %s  （时长 %.0fs | 注视 %.0f%%）" % (
        s["name"], s["duration"], s["looking_pct"]))

    # 2) 状态区间带
    y0, y1 = 0, 1
    color = {"Idle": "#dddddd", "Guiding": "#ffd27f", "Hidden": "#8fd18f", "Feedback": "#7fb2ff"}
    last_t, last_state = 0.0, "Idle"
    for e in s["events"]:
        if e.get("type") == "StateChange":
            det = str(e.get("detail", ""))
            cur = det.split("->")[-1].strip() if "->" in det else det.strip()
            ax[1].axvspan(last_t, e.get("t", last_t), color=color.get(last_state, "#eeeeee"))
            last_t, last_state = e.get("t", last_t), cur
        elif e.get("type") == "SessionEnd":
            ax[1].axvspan(last_t, e.get("t", last_t), color=color.get(last_state, "#eeeeee"))
            last_t = e.get("t", last_t)
    if samples:
        ax[1].axvspan(last_t, samples[-1]["t"], color=color.get(last_state, "#eeeeee"))
    ax[1].set_yticks([])
    ax[1].set_ylabel("状态")
    from matplotlib.patches import Patch
    ax[1].legend(handles=[Patch(color=v, label=k) for k, v in color.items()],
                 loc="upper right", ncol=4, fontsize=8)

    # 3) 性能
    ax2 = ax[2]
    ax2.plot(ts, [r["fps"] for r in samples], label="FPS", color="#2a7f2a")
    ax2.set_ylabel("FPS", color="#2a7f2a")
    ax2b = ax2.twinx()
    ax2b.plot(ts, [r["inferMs"] for r in samples], label="推理ms", color="#a33", alpha=0.8)
    ax2b.set_ylabel("推理 ms", color="#a33")
    ax2.set_xlabel("t (秒)")

    fig.tight_layout()
    plots = os.path.join(outdir, "plots")
    os.makedirs(plots, exist_ok=True)
    path = os.path.join(plots, "session_%s.png" % s["name"])
    fig.savefig(path, dpi=110)
    plt.close(fig)
    return path


# ---------- 主流程 ----------

def run(root, outdir, do_plot=True):
    sessions = find_sessions(root)
    if not sessions:
        print("未找到会话目录（应包含 session.json）：", root)
        return 1

    os.makedirs(outdir, exist_ok=True)
    results = [analyze_session(d) for d in sessions]

    # summary.csv
    with open(os.path.join(outdir, "summary.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["session", "duration_s", "looking_pct", "speaking_pct", "target_pct",
                    "fps_avg", "infer_avg_ms", "infer_p95_ms", "guides", "feedbacks",
                    "level", "device"])
        for s in results:
            c = s["event_counts"]
            w.writerow([s["name"], "%.1f" % s["duration"], "%.1f" % s["looking_pct"],
                        "%.1f" % s["speaking_pct"], "%.1f" % s["target_pct"],
                        "%.0f" % s["fps_avg"], "%.0f" % s["infer_avg"], "%.0f" % s["infer_p95"],
                        c.get("GuideStart", 0), c.get("Feedback", 0),
                        s["meta"].get("settings", {}).get("levelName", s["meta"].get("interventionLevel", "?")),
                        s["meta"].get("device", "?")])

    # summary.md
    lines = ["# 会话数据分析", "",
             "会话数：%d ｜ 总时长：%.0f 秒" % (len(results), sum(s["duration"] for s in results)), "",
             "| 会话 | 时长 | 注视占比 | 讲话占比 | 目标可见 | FPS | 推理均耗 | 事件 |",
             "|---|---|---|---|---|---|---|---|"]
    lines += [fmt_row(s) for s in results]
    lines += ["", "## 各状态累计时长（秒）", ""]
    for s in results:
        st = s["state_seconds"]
        lines.append("- %s：待机%.0f / 引导%.0f / 隐藏%.0f / 反馈%.0f" %
                     (s["name"], st.get("Idle", 0), st.get("Guiding", 0),
                      st.get("Hidden", 0), st.get("Feedback", 0)))
    if do_plot:
        lines += ["", "## 图", ""]
        for s in results:
            r = plot_session(s, outdir)
            if isinstance(r, str) and r.endswith(".png"):
                lines.append("- plots/session_%s.png" % s["name"])
            else:
                lines.append("- %s %s" % (s["name"], r))

    with open(os.path.join(outdir, "summary.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))

    print("会话数:", len(results))
    for s in results:
        print(fmt_row(s))
    print("输出:", os.path.join(outdir, "summary.md"))
    return 0


# ---------- 自测（合成数据） ----------

def selftest():
    import tempfile
    root = tempfile.mkdtemp(prefix="aranalyze_")
    sess = os.path.join(root, "20260101_120000")
    os.makedirs(sess)
    with open(os.path.join(sess, "session.json"), "w", encoding="utf-8") as f:
        json.dump({"startedAt": "2026-01-01 12:00:00", "interventionLevel": 1,
                   "device": "Oculus Quest 3", "settings": {"level": 1, "levelName": "中"}}, f)
    with open(os.path.join(sess, "samples.csv"), "w", encoding="utf-8", newline="") as f:
        f.write("t,looking,speaking,targetValid,fps,inferMs\n")
        for i in range(30):
            t = i * 2.0
            looking = 1 if 10 <= i <= 25 else 0
            f.write("%.2f,%d,1,1,72.0,%.1f\n" % (t, looking, 190 + (i % 5) * 3))
    with open(os.path.join(sess, "events.jsonl"), "w", encoding="utf-8") as f:
        ev = [{"t": 0.0, "type": "SessionStart", "detail": ""},
              {"t": 4.0, "type": "StateChange", "detail": "Idle->Guiding"},
              {"t": 4.0, "type": "GuideStart", "detail": "auto"},
              {"t": 10.0, "type": "GazeStart", "detail": ""},
              {"t": 12.0, "type": "GazeSustained", "detail": ""},
              {"t": 30.0, "type": "SpeechEnd", "detail": ""},
              {"t": 30.0, "type": "StateChange", "detail": "Hidden->Feedback"},
              {"t": 32.0, "type": "StateChange", "detail": "Feedback->Idle"},
              {"t": 58.0, "type": "SessionEnd", "detail": ""}]
        for e in ev:
            f.write(json.dumps(e, ensure_ascii=False) + "\n")

    outdir = os.path.join(root, "analysis")
    rc = run(root, outdir, do_plot=True)
    assert rc == 0, "run 失败"
    for name in ("summary.md", "summary.csv"):
        assert os.path.exists(os.path.join(outdir, name)), name + " 未生成"
    md = open(os.path.join(outdir, "summary.md"), encoding="utf-8").read()
    assert "20260101_120000" in md and "注视" in md
    print("✅ 自测通过：合成会话分析 + 输出文件齐备")
    return 0


def main():
    ap = argparse.ArgumentParser(description="AR 注意力训练会话分析")
    ap.add_argument("root", nargs="?", help="sessions 目录")
    ap.add_argument("--out", default=None, help="输出目录（默认 <sessions>/../analysis）")
    ap.add_argument("--no-plot", action="store_true", help="不出图")
    ap.add_argument("--selftest", action="store_true", help="用合成数据自测")
    args = ap.parse_args()

    if args.selftest:
        return selftest()
    if not args.root:
        ap.print_help()
        return 2
    outdir = args.out or os.path.join(os.path.dirname(os.path.abspath(args.root)), "analysis")
    return run(args.root, outdir, do_plot=not args.no_plot)


if __name__ == "__main__":
    sys.exit(main())
