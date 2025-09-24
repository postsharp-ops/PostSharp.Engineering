# --- Settings ---
$TaskName   = 'BuildAgent Daily Maintenance'
$ScriptPath = "PSScriptRoot\Daily-Maintenance.ps1"
$DailyAt    = '03:00'

# --- Define action (run as SYSTEM) ---
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File  $ScriptPath"

# --- Triggers: at startup + daily ---
$startupTrigger = New-ScheduledTaskTrigger -AtStartup
$dailyTrigger   = New-ScheduledTaskTrigger -Daily -At ([datetime]::Parse($DailyAt))

# --- Settings: run if missed, limit runtime, tolerate overlap ---
$settings = New-ScheduledTaskSettingsSet `
  -StartWhenAvailable `
  -RunOnlyIfNetworkAvailable `
  -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
  -MultipleInstances IgnoreNew `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries

# --- Principal: SYSTEM, highest privileges ---
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

# --- Create/overwrite the task ---
$task = New-ScheduledTask -Action $action -Trigger @($startupTrigger,$dailyTrigger) -Settings $settings -Principal $principal
Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force
