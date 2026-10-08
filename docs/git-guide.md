# Git 流程卡（团队协作手册）

> 适用：ARAttentionTrainer 全体成员 ｜ 更新：2026-10-08
> ⚡ 状态：本地版本管理已启用；云端 GitHub 仓库待建（等代理节点恢复后补，见第三节）

## 一、核心概念（30 秒）

- **git**：电脑上的存档系统。`commit` = 一个存档点（带说明），仓库 = 全部存档
- **GitHub**：云端仓库。`push` 上传存档（备份+组员可见），`pull` 拉取别人的新存档
- **分布式**：本地是完整的！断网也能 commit，网络好了统一 push 即可

## 二、日常四步循环（最常用）

```bash
git status                    # ① 看改了什么（红色=没存档，绿色=待提交）
git add -A                    # ② 全部放入暂存区
git commit -m "feat: 说明"    # ③ 存档（说明格式见第五节）
git push                      # ④ 上传云端（云端仓库建好后可用）
```

## 三、常用命令速查

| 命令 | 作用 |
|------|------|
| `git status` | 查看当前状态（最常用！不确定就先敲它） |
| `git log --oneline` | 查看存档历史 |
| `git diff` | 看具体改了哪些行（还没 add 时） |
| `git add <文件>` | 只暂存某个文件（`-A` 是全部） |
| `git pull` | 拉取云端新存档（**每次开工前先敲**） |

## 四、云端仓库（⚡ 待办：代理节点恢复后执行）

1. github.com → **+** → New repository → 名称 `ARAttentionTrainer` → **Private** → 三个初始化勾**全不勾** → Create
2. 绑定并首次推送（只做一次）：

```bash
cd /d/Development/Unity/ARAttentionTrainer
git remote add origin https://github.com/<用户名>/ARAttentionTrainer.git
git push -u origin main
# 弹窗选 "Sign in with your browser" 浏览器授权
```

3. 邀请组员：仓库网页 → Settings → Collaborators → Add people（输组员 GitHub 用户名）
4. 组员首次使用：`git clone <仓库地址>` → Unity Hub 添加该文件夹 → 以后开工前 `git pull`

## 五、提交规范

- 说明格式：`feat:` 新功能 ｜ `fix:` 修bug ｜ `chore:` 杂务 ｜ `docs:` 文档 ｜ `test:` 测试
- 例：`feat: 人脸检测管线跑通，视频源上框住人脸`
- **小步提交**：完成一个小模块就提交一次（将来结题可以回看开发历程）

## 六、常见问题

| 问题 | 处理 |
|------|------|
| push 超时/连不上 | 检查代理软件是否运行（git 已配置走 127.0.0.1:7890）；换节点后重试 |
| 中文文件名显示成转义字符 | `git config --global core.quotepath false` |
| 弹窗要密码/Token 而不是浏览器授权 | 说明 token 过期，找李然换新 token |
| 提示冲突（conflict） | **不要乱删文件**，先截图发群里，让李然处理 |
| 不小心改坏了文件 | `git status` + `git diff` 找到改了什么；实在不行发群里 |

## 七、红线

- ❌ 不要删除 `.git` 文件夹（那是整个存档系统）
- ❌ 不要提交 `Library/`、`Temp/`、`Builds/`（.gitignore 已挡，别绕过）
- ❌ 不要 `git push --force`（会覆盖别人的存档）
