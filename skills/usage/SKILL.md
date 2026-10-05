---
name: usage
description: 在 Windows Codex 的麦克风位置显示原生用量圆圈，点击查看本对话 context 和账号小时、周额度。适用于“查看用量”“显示额度”“打开用量圆圈”等请求。
---

在前台 exec_command 中运行 node 加上本技能目录内 scripts/open.mjs 的绝对路径。当前运行时提供准确主对话 ID 时可传入 --thread-id；脚本会启动可见的原生 GUI，结束启动命令后 GUI 继续运行。已有组件不会重复启动。不要安装 SessionStart hook。

组件通过 Codex 本地 IPC 跟随选中的对话，在输入框麦克风位置显示双圆圈，点击展开详情，右键菜单可以退出。无法确认对话或麦克风位置时保留未知状态；不要根据最新日志猜测对话，也不要把“进程已启动”表述成“圆圈已经可见”。

默认使用原生 GUI。只有用户明确需要网页或 MCP Apps 面板时才调用 open_usage_rings 并打开它返回的 browserUrl。
