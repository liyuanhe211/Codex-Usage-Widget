# Codex Usage Widget

An unofficial Windows widget for the Codex coding mode of the ChatGPT desktop app. Two small rings in the composer show how much context the selected conversation uses and how much of your account's usage limits is spent.

Codex Usage Widget is an independent project. It is not affiliated with, endorsed by, or sponsored by OpenAI. ChatGPT and Codex are trademarks of OpenAI.

## Installation

Install the ChatGPT desktop app on Windows x64 and sign in. Put the complete project in a permanent folder, then double-click `Setup.cmd`. Setup prepares the runtime, installs dependencies, builds the widget, and enables autostart.

You can also open the project folder in Codex and paste these instructions:

```markdown
Install the Codex Usage Widget from the current project.

1. Confirm that the current folder contains the complete project, including Setup.ps1, package.json, native, scripts, and public. Check for Windows x64, a signed-in ChatGPT desktop app, and the required runtime. Keep any existing widget position settings.
2. In the project folder, run powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\Setup.ps1. Allow setup to download the official runtime and dependencies, build the native widget, and enable autostart. Do not skip any verification step.
3. Read Setup_Report_Private.json and confirm that success is true. If setup fails, keep the error log and fix the specific problem.
4. Verify that the native widget and the autostart supervisor are running, then check whether the rings actually appear in the current conversation. If you cannot confirm the interface directly, report what you verified instead of claiming that the rings are visible because the processes exist.
```

Setup needs an internet connection. When copying the project to another computer, you can omit `node_modules`, `dist`, `native/build`, `_Claude_tmp`, and generated `*_Private.json` files; keep all source folders.

![Usage Rings with the native detail card in dark appearance](docs/images/widget-dark.png)

*Preview with sample data. The rings and the detail card are drawn by the current native widget; the surrounding composer is a mock-up.*

## Read the rings

| Element | Meaning |
|---|---|
| Outer arc | Context used by the selected conversation, divided by its model's context capacity |
| Filled inner circle | The higher used percentage of the account's short-term and weekly limits |
| Unfilled area | Transparent; zero usage draws no colored area |
| Missing data | Details show unavailable or an em dash, never a false zero |

The inner circle fills counterclockwise from the top. A full circle means 100% used.

All percentages are rounded down to whole numbers; for example, `99.9%` is shown as `99%`. Ring fill, color thresholds, and the exhaustion forecast still use the raw values. Missing data is still shown as unknown.

Click the rings to open or close details. Click outside the card or press Escape to dismiss it. Accounts without a short-term limit do not get an empty 5-hour row.

Reset times use the local time zone, for example **`Reset at Oct 11 20:53 (6 d 20 h)`**. When less than a day remains, the countdown in parentheses shows only hours, for example `3 h`; when less than an hour remains, it shows minutes, for example `35 m`; when less than a minute remains, it shows `<1 m`.

**Credits are usage units, not a dollar balance.** The widget displays the interface's credit count without a dollar sign or an assumed conversion. Purchase prices depend on the plan or agreement. [Official credits explanation](https://learn.chatgpt.com/docs/pricing#what-are-tokens-and-credits)

## Choose a position

![Rings beside Model and over Mic](docs/images/positions.png)

The **Position: Model / Mic** switch shares the bottom row with **Credits remaining**.

| Switch position | Widget location |
|---|---|
| Left / Model | Immediately left of the model selector |
| Right / Mic | Over the microphone, to the right of the model selector |

Both states use the same blue track and readable labels. The white knob indicates the selected side.

Changing the switch moves the rings immediately and saves the choice to `Usage_Widget_Settings_Private.json`. The open detail card stays in place. Clicking the rings again opens the card at the new position; dismissing and reopening it does the same.

The widget follows the desktop window and selected conversation. Background sampling spans the composer toolbar and excludes pixels covered by input-method candidates or other windows. If the model selector cannot be located, Model placement temporarily falls back to the microphone.

## Light appearance and thresholds

![Usage Rings and details in light appearance](docs/images/widget-light.png)

| Meter | Normal | Yellow | Red |
|---|---|---|---|
| Context, model capacity below 280,000 | Blue below 180,000 tokens | 180,000 to below 220,000 tokens | 220,000 tokens or more |
| Context, model capacity above 600,000 | Blue below 300,000 tokens | 300,000–400,000 tokens, inclusive | Above 400,000 tokens |
| Account usage | Green below 70% | 70%–85%, inclusive | Above 85% |

Context arc length uses the reported capacity; its color uses the absolute token count and the capacity tier. Capacities from 280,000 through 600,000, or an unavailable capacity, retain the 300,000/400,000 thresholds.

## Weekly usage forecast

The forecast appears on the right, below the weekly percentage, in the same font size as the reset time. The widget derives the average usage rate from the share of the weekly limit used so far and the time elapsed in the current period, and assumes that the rate continues. The period start is the reset time minus the reported period length. Actual future usage may differ.

If the limit is projected to run out before the reset, the widget compares two durations: from now until exhaustion, and from exhaustion until the reset. It shows only the shorter one; when they are equal, it shows the time until exhaustion. The comparison uses exact seconds and is not affected by how the displayed days, hours, and minutes are rounded.

| Text | Shown when |
|---|---|
| `Runs out in 3 h` | The time until exhaustion is shorter, or the two durations are equal |
| `Runs out 3 h before reset` | The time from exhaustion to the reset is shorter |
| `Runs out in 35 m` / `Runs out 35 m before reset` | The selected duration is under an hour, so minutes are shown |
| `Will consume 70% before reset.` | The limit is projected to last until the reset; the value is the projected total usage of the period at the reset |
| No forecast text | There is no usage yet, or the used share, period length, reset time, or elapsed time is insufficient for an estimate |

The forecast and the reset countdown use the same duration format. Zero days are omitted; under an hour, only minutes are shown; under a minute, `<1 m` is shown. Displayed days, hours, and minutes are rounded down.

| Remaining time | Display |
|---|---|
| 1 day 10 hours | `1 d 10 h` |
| 3 hours 30 minutes | `3 h` |
| 35 minutes 30 seconds | `35 m` |
| 20 seconds | `<1 m` |

The screenshots below use sample data and are drawn directly by the current native widget. Their reset times were computed from the local time when the screenshots were generated.

| Exhaustion comes sooner: time until exhaustion | Reset comes sooner: time left before reset |
|---|---|
| ![Projected to run out in three hours](docs/images/forecast-exhaustion.png) | ![Projected to run out three hours before reset](docs/images/forecast-before.png) |

| Reset countdown and forecast in minutes | Time before reset in minutes |
|---|---|
| ![Reset in thirty-five minutes, projected to run out in ten minutes](docs/images/forecast-minutes.png) | ![Projected to run out thirty-five minutes before reset](docs/images/forecast-minute-gap.png) |

| No usage: no forecast | Lasts until reset: projected total usage |
|---|---|
| ![No forecast text without usage](docs/images/forecast-zero.png) | ![Projected to use seventy percent of the limit before reset](docs/images/forecast-consumption.png) |

## Silent startup

After setup, a native supervisor starts at Windows sign-in and checks the supported desktop app every two seconds. It starts the widget with the app, reuses it across new conversations, and restores it if it exits. Failed launches retry after at least 15 seconds.

Normal startup opens no console or webpage and sends no conversation messages. Each program allows one instance in the current Windows session. Autostart uses a current-user Startup shortcut; preferences use project JSON files.

Keep the project folder in place. If you move it, run `Setup.cmd` from the new location.

To disable autostart and recovery, run from the project folder:

~~~powershell
npm run autostart:remove
~~~

The current widget stays open. Quit it from the rings' right-click menu or the system tray menu. With autostart enabled, quitting only the widget makes the supervisor restore it.

If setup installed portable Node.js and `npm` is unavailable in your terminal:

~~~powershell
$deployment = Get-Content .\native\Local_Deployment_Private.json -Raw | ConvertFrom-Json
& $deployment.nodeExecutable .\scripts\install-autostart.mjs --remove
~~~

## Data and compatibility

The native widget reads the bound local conversation's numeric usage events every second; the browser view reads them every five seconds. Context uses the latest request's `last_token_usage.total_tokens` and `model_context_window`, not cumulative billed tokens. The first read scans the bound log; later reads consume only new complete records, and large tool outputs do not erase earlier usage. Before Codex reports the conversation's first usage event, the native details show `Waiting for first usage`; while waiting for a new event after compaction, they show `Waiting for usage update`. Account limits refresh independently in the background and never delay the native widget's context values.

Account limits use the read-only `account/rateLimits/read` interface, cached for up to one minute. A valid recorded quota may be used if live data is unavailable; the card's tooltip identifies recorded data. Expired records stop filling the inner circle.

The native widget reads the accessibility title of the current page and looks up the unique matching conversation ID in the read-only local conversation index. It verifies the page when it starts and when the window is restored, so you do not need to switch conversations first. When the page changes, it clears the old context and discards delayed results that belong to the previous page. A blank new page, conversations with duplicate names, multiple windows, and pages that cannot be verified all show unknown; a new conversation shows actual usage once it receives a numeric usage event. The widget resolves a subagent's parent conversation through verified local session metadata. It never guesses the current conversation from file modification times and never reads conversation content or sign-in credentials.

This is an independent native overlay, not an official composer extension. It does not patch the desktop app. Recognition and IPC may need adjustments after desktop updates. Composer buttons are recognized in English and Chinese interfaces. Supported installations are the ChatGPT desktop app for Windows, whose package is still named `OpenAI.Codex` because it replaced the standalone Codex app, and the recognized legacy standalone Codex installation. Other applications named ChatGPT are not supported.

## Troubleshooting

| Symptom | Check |
|---|---|
| Desktop app not found | Install the ChatGPT desktop app for Windows under the same Windows user; rerun setup |
| Rings are hidden | Bring the app to the foreground with a Codex conversation composer visible; use an English or Chinese interface |
| Context is waiting or unavailable | `Waiting for first usage` means Codex has not yet reported this conversation's first usage. `Waiting for usage update` means the widget is waiting for a new event after compaction. `Conversation not confirmed` means the conversation has not been identified yet. Read failures still show `Unavailable`. Select a local conversation in one supported window |
| Account usage unavailable | Check the signed-in account; allow a minute for refresh |
| Position is not saved | Make sure the project folder is writable |
| Startup fails after moving the folder | Run `Setup.cmd` in the new location |

Diagnostic files are excluded from version control: `Setup_Report_Private.json`, `Autostart_Installation_Private.json`, `_Claude_tmp/Native_Widget_State_Private.json`, and `_Claude_tmp/Autostart_State_Private.json`.

## Development and optional plugin

With Node.js 24 or newer already available:

~~~powershell
npm ci
npm run native
~~~

`npm run native:build` rebuilds the native widget. The built `native/build/CodexUsageRings.exe` accepts `--render-test <output-directory>`, which checks ring rendering and generates dark and light detail images.

The automated test suite is not included in this repository. Setup runs it only when a `tests` folder is present.

Setup installs the native widget and autostart directly. The optional plugin adds a manual skill and MCP entry:

~~~powershell
node build.mjs
codex plugin marketplace add . --json
codex plugin add codex-usage-rings@usage-rings-local --json
~~~

`plugin.json` is the current manifest; `.codex-plugin/plugin.json` supports the older layout. `USAGE_RINGS_CODEX_COMMAND` overrides the CLI path; existing `CODEX_HOME` settings are respected. Legacy browser/MCP Apps views remain an explicit manual choice.

The setup fallback CLI is pinned to 0.160.0 for protocol compatibility.

## License

Copyright © 2026 Yuanhe Li.

This project is licensed under the [Creative Commons Attribution-NonCommercial 4.0 International License](https://creativecommons.org/licenses/by-nc/4.0/) (CC BY-NC 4.0). You may share and adapt it for non-commercial purposes, provided that you credit Yuanhe Li, link to this repository and the license, and indicate any changes. Commercial use requires separate written permission from the author. See [LICENSE](LICENSE) for the full terms.

## References

- [ChatGPT desktop app](https://learn.chatgpt.com/docs/app)
- [OpenAI plugin installation](https://developers.openai.com/plugins/build/plugins)
- [OpenAI UI extensions](https://developers.openai.com/plugins/build/extensions)
- [Codex app-server](https://developers.openai.com/codex/app-server)
- [CodexUsage](https://github.com/jayhilwig/codexusage), design reference for the native overlay
- [CodexTokenOverlay](https://github.com/soleillevant0125/codex-token-overlay), design reference for conversation identification
