# Private AI Timesheets for Mac

## Concept

Build a private Mac app that turns a user's workday into draft time entries and asks them to confirm before anything is submitted to their time tracker.

The idea is inspired by Cotypist's product shape: ambient, local, privacy-first assistance that quietly helps in the background. Instead of autocomplete, this app helps users reconstruct and log billable time.

## One-line positioning

> A private Mac timesheet copilot that notices what you worked on, suggests the right project, and helps you confirm time entries before they hit your tracker.

## Short pitch

Stop reconstructing your workday from memory. The app runs quietly on your Mac, observes local activity signals, matches them against your projects, and creates draft time entries. At the end of the day, or when it detects a likely project, it asks you to confirm, edit, or discard before submitting to Clockodo, Toggl, Harvest, or other trackers.

## Core principles

1. **Draft, don't decide**
   - The app suggests time entries.
   - It does not automatically bill clients without user approval.

2. **Privacy-first**
   - Local model.
   - Local activity history.
   - Optional screenshots/OCR.
   - No server-side analysis by default.

3. **Low interruption**
   - End-of-day review is the main workflow.
   - Real-time prompts should happen only when confidence is high.

4. **Tracker-agnostic over time**
   - Start with Clockodo.
   - Add more trackers through a common MCP-backed integration layer.

5. **Learn from corrections**
   - Every correction improves future project matching.

## MVP: Clockodo-only Mac app

### 1. Mac menu-bar app

A lightweight app that runs in the background.

Basic menu:

- Start/stop observing
- Open timeline
- Review today
- Settings
- Privacy mode
- Sync with Clockodo

### 2. Clockodo integration

Use the existing Clockodo MCP server to:

- fetch customers
- fetch projects
- fetch services/tasks
- start/stop clocks
- create time entries
- update/delete time entries if needed

Initial scope: submit confirmed completed time entries.

### 3. Local activity timeline

The app records local activity signals:

- active app name
- window title
- browser domain/title
- current repo/folder if detectable
- idle periods
- app switch events
- optional calendar context later

Example raw activity:

```text
09:10-10:25
App: Cursor
Window: clockodo-mcp - ClockodoTools.cs
Browser: docs.clockodo.com
Repo: alainkaiser/clockodo-mcp
```

### 4. End-of-day review popup

User configures a daily review time, for example 17:30.

At that time, the app shows:

> Here's what I think you worked on today. Want to review and submit?

Then it displays draft entries:

```text
09:10-10:25 - Clockodo MCP / Development
Worked on Clockodo MCP tool hardening and tests.

10:30-11:00 - Internal / Planning
Reviewed product idea and roadmap.
```

User can:

- accept
- edit project/customer/service
- edit description
- split block
- merge blocks
- discard
- submit to Clockodo

This should be the core v1 feature.

### 5. Local project matching

The app compares observed work context against available Clockodo data.

Inputs:

- app/window title
- browser URL/title
- repo/folder name
- recent user corrections
- available customers/projects/services
- recent time entries
- optional calendar events

Output:

```json
{
  "customer": "Acme",
  "project": "Website Relaunch",
  "service": "Development",
  "confidence": 0.87,
  "reason": "Cursor repo and browser tabs matched Acme project keywords."
}
```

### 6. Optional active nudges

If confidence is high during the day:

> Looks like you're working on Acme / Website Relaunch. Start timer?

Actions:

- Start timer
- Not this project
- Ignore for 1h
- Never ask for this app/site
- Always suggest this project

Important: prompts should be optional and conservative. The app should not become annoying.

### 7. Local model support

During onboarding:

- user downloads a local model
- app explains that analysis runs locally
- user can disable AI and use rules only
- all activity stays local by default

Local model responsibilities:

- summarize activity blocks
- match activity to project/service
- generate time entry descriptions
- learn from corrections

### 8. Privacy controls

Must-have settings:

- exclude apps
- exclude websites/domains
- pause tracking
- delete local history
- disable screenshots/OCR
- local-only mode
- show exactly what was captured

Screenshots/OCR should be opt-in, not default.

## Suggested v1 user flow

### Onboarding

1. Install Mac app.
2. Grant Accessibility permission.
3. Connect Clockodo.
4. Download local model.
5. Choose work hours.
6. Choose daily review time.
7. Select privacy exclusions.
8. App fetches Clockodo projects/services.

### Daily usage

1. User works normally.
2. App records local activity.
3. Optional: app asks to start timer when confidence is high.
4. At configured time, app opens daily review.
5. User confirms/edits suggested entries.
6. App submits entries to Clockodo.

## What to avoid in v1

Avoid:

- automatic billing without confirmation
- too many time trackers
- team dashboards
- invoicing
- web SaaS admin panel
- aggressive real-time popups
- always-on screenshots by default
- Cotypist-style inline overlays
- complex cross-platform support

The first version should be narrow:

> Mac + Clockodo + local timeline + end-of-day review + confirm-to-submit.

## Future roadmap

### Phase 2: More trackers

Add:

- Toggl Track
- Harvest
- Clockify
- Jira Tempo
- Linear/Jira context
- Google Calendar / Apple Calendar

Use a common integration model:

```text
list_customers
list_projects
list_services
create_time_entry
start_timer
stop_timer
update_time_entry
delete_time_entry
```

MCP can power this integration layer.

### Phase 3: Smarter learning

- project aliases
- correction memory
- automatic confidence tuning
- per-client rules
- repo-to-project mapping
- browser-domain-to-project mapping
- calendar-to-project mapping

### Phase 4: Stronger local AI

- better local model options
- optional OCR/screenshot context
- daily summary generation
- automatic work block grouping
- "why did you suggest this?" explanations

### Phase 5: Team/SaaS layer

Only after the individual workflow works:

- shared tracker integrations
- team policies
- admin settings
- billing
- organization plans

## Key risks

### 1. Trust

Users will not trust automatic time entries unless they can review and correct them.

Mitigation:

- always show drafts
- explain why a project was suggested
- make editing fast
- learn from corrections

### 2. Privacy

The app observes user activity, which is sensitive.

Mitigation:

- local-first
- clear permissions
- opt-in screenshots
- app/domain exclusions
- delete data button
- no cloud analysis by default

### 3. Interruption fatigue

Too many popups will annoy users.

Mitigation:

- end-of-day review first
- nudges only above high confidence
- snooze/ignore controls

### 4. Tracker complexity

Each time-tracking tool has different concepts.

Mitigation:

- start with Clockodo
- design a minimal common model
- add integrations slowly

## Product framing

### Category

Private AI timesheets for Mac.

### Tagline options

- Turn your workday into confirmed time entries.
- Your private Mac assistant for billable time.
- Review your day. Confirm your time. Submit to your tracker.

### Landing page headline

> Stop reconstructing your day from memory.

### Landing page subheadline

> A private Mac app that observes your work locally, drafts time entries, and lets you confirm them before submitting to Clockodo and other trackers.

## Recommended first build

Build this first:

1. Mac menu-bar app
2. Clockodo integration
3. local activity capture
4. local timeline
5. end-of-day review popup
6. project/service suggestions
7. manual confirm/edit/submit
8. basic privacy exclusions

If this feels useful for one real Clockodo user, then expand.
