---
name: usage
description: Shows native usage rings in the composer of the ChatGPT desktop app on Windows. Click them for the current conversation's context and the account's short-term and weekly limits. Use for requests such as "show usage", "show my limits", or "open the usage rings".
---

Run node with the absolute path of scripts/open.mjs in this skill's directory, in a foreground exec_command. When the runtime provides the exact main conversation ID, pass it with --thread-id. The script starts the visible native GUI, which keeps running after the launch command ends. An already running widget is not started twice. Do not install a SessionStart hook.

The widget follows the selected conversation through the desktop app's local IPC, draws the two rings in the composer beside the model selector or over the microphone button, opens details on click, and quits from its right-click menu. When the conversation or the button position cannot be confirmed, it keeps the unknown state. Do not guess the conversation from the newest log, and do not report "the process started" as "the rings are visible".

Use the native GUI by default. Call open_usage_rings and open the browserUrl it returns only when the user explicitly asks for a web page or MCP Apps panel.