use serde::Serialize;
use serde_json::Value;
use std::{
    collections::HashMap,
    io::{BufRead, BufReader, Write},
    path::PathBuf,
    process::{Command, Stdio},
    sync::Mutex,
    thread,
};
use tauri::{AppHandle, Emitter, Manager};

const COMMANDS: &[&str] = &[
    "get-state",
    "provider-defaults",
    "save-provider",
    "oauth-openrouter",
    "models",
    "scan",
    "run-create",
    "run-get",
    "run-list",
    "run-reconcile",
    "run-resume-after-restart",
    "run-keep",
    "analyze-local",
    "diagnose",
    "actions",
    "apply",
    "history",
    "rollback",
    "measurement-workloads",
    "measurement-start",
    "measurement-stop",
    "measurement-cancel",
    "measurement-analyze",
    "measurement-frame-import",
    "measurement-list",
    "measurement-compare",
    "measurement-topology",
    "measurement-gpu-candidates",
    "measurement-gpu-affinity-inspect",
    "measurement-delete",
    "power-plan-list",
    "power-plan-stage",
];

#[derive(Default)]
struct AgentState(Mutex<HashMap<String, ActiveAgent>>);

struct ActiveAgent {
    process_id: u32,
    cancellable: bool,
    cancelled: bool,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct ProgressEvent<'a> {
    request_id: &'a str,
    message: &'a str,
}

#[tauri::command]
async fn agent(
    app: AppHandle,
    request_id: String,
    command: String,
    payload: Option<Value>,
) -> Result<Value, String> {
    validate_request(&request_id)?;
    if !COMMANDS.contains(&command.as_str()) {
        return Err("Unsupported NeuroTune agent command".into());
    }
    tauri::async_runtime::spawn_blocking(move || run_agent(&app, &request_id, &command, payload))
        .await
        .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn cancel_agent(app: AppHandle, request_id: String) -> Result<bool, String> {
    validate_request(&request_id)?;
    cancel_request(&app.state::<AgentState>().0, &request_id)
}

fn cancel_request(
    active_agents: &Mutex<HashMap<String, ActiveAgent>>,
    request_id: &str,
) -> Result<bool, String> {
    let process_id = {
        let mut active = active_agents
            .lock()
            .map_err(|_| "Agent state is unavailable")?;
        let Some(agent) = active.get_mut(request_id) else {
            return Ok(false);
        };
        if !agent.cancellable {
            return Err("Only a NeuroTune scan or measurement analysis can be cancelled".into());
        }
        agent.cancelled = true;
        agent.process_id
    };

    if let Err(error) = terminate_process_tree(process_id) {
        if let Ok(mut active) = active_agents.lock() {
            if let Some(agent) = active.get_mut(request_id) {
                agent.cancelled = false;
            }
        }
        return Err(error);
    }
    Ok(true)
}

fn terminate_process_tree(process_id: u32) -> Result<(), String> {
    let process_id = process_id.to_string();
    let mut last_error = String::new();
    for _ in 0..3 {
        let output = Command::new("taskkill.exe")
            .args(["/PID", &process_id, "/T", "/F"])
            .creation_flags(0x08000000)
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .output()
            .map_err(|error| format!("Could not cancel the NeuroTune scan: {error}"))?;
        if output.status.success() {
            return Ok(());
        }
        if !output.stderr.is_empty() {
            last_error = String::from_utf8_lossy(&output.stderr).trim().to_string();
        }
        thread::sleep(std::time::Duration::from_millis(50));
    }
    Err(format!(
        "Windows could not terminate the NeuroTune scan process tree{}",
        if last_error.is_empty() {
            String::new()
        } else {
            format!(": {last_error}")
        }
    ))
}

fn run_agent(
    app: &AppHandle,
    request_id: &str,
    command: &str,
    payload: Option<Value>,
) -> Result<Value, String> {
    let executable = agent_path(app)?;
    let mut child = Command::new(&executable)
        .arg(command)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .creation_flags(0x08000000)
        .spawn()
        .map_err(|error| {
            format!(
                "Could not start NeuroTune.Agent at {}: {error}",
                executable.display()
            )
        })?;
    {
        let state = app.state::<AgentState>();
        let mut active = state.0.lock().map_err(|_| "Agent state is unavailable")?;
        if active
            .insert(
                request_id.to_string(),
                ActiveAgent {
                    process_id: child.id(),
                    cancellable: is_cancellable(command),
                    cancelled: false,
                },
            )
            .is_some()
        {
            let _ = child.kill();
            return Err("Duplicate agent request ID".into());
        }
    }

    if let Some(mut stdin) = child.stdin.take() {
        if let Err(error) = stdin.write_all(payload.unwrap_or(Value::Null).to_string().as_bytes()) {
            let _ = child.kill();
            if let Ok(mut active) = app.state::<AgentState>().0.lock() {
                active.remove(request_id);
            }
            return Err(format!("Could not send the agent request: {error}"));
        }
    }
    let stderr = child.stderr.take();
    let progress_app = app.clone();
    let progress_request_id = request_id.to_string();
    let progress = thread::spawn(move || {
        let mut messages = Vec::new();
        if let Some(stderr) = stderr {
            for line in BufReader::new(stderr).lines().map_while(Result::ok) {
                let _ = progress_app.emit(
                    "agent-progress",
                    ProgressEvent {
                        request_id: &progress_request_id,
                        message: &line,
                    },
                );
                messages.push(line);
            }
        }
        messages.join("\n")
    });
    let output = child.wait_with_output().map_err(|error| error.to_string());
    let cancelled = app
        .state::<AgentState>()
        .0
        .lock()
        .map_err(|_| "Agent state is unavailable")?
        .remove(request_id)
        .is_some_and(|agent| agent.cancelled);
    let output = output?;
    let stderr = progress.join().unwrap_or_default();
    if cancelled {
        return Err("Agent request cancelled".into());
    }
    let response: Value = serde_json::from_slice(&output.stdout)
        .map_err(|_| format!("The NeuroTune agent returned an invalid response. {stderr}"))?;
    if response.get("ok").and_then(Value::as_bool) == Some(true) {
        Ok(response.get("data").cloned().unwrap_or(Value::Null))
    } else {
        Err(response
            .get("error")
            .and_then(Value::as_str)
            .unwrap_or("The NeuroTune agent failed")
            .to_string())
    }
}

fn is_cancellable(command: &str) -> bool {
    matches!(command, "scan" | "measurement-analyze")
}

fn validate_request(request_id: &str) -> Result<(), String> {
    if request_id.is_empty()
        || request_id.len() > 128
        || !request_id
            .bytes()
            .all(|value| value.is_ascii_alphanumeric() || value == b'-')
    {
        return Err("Invalid agent request ID".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{
        cancel_request, is_cancellable, validate_request, ActiveAgent, AgentState, COMMANDS,
    };
    #[cfg(target_os = "windows")]
    use std::os::windows::process::CommandExt;
    use std::{
        io::{BufRead, BufReader},
        process::{Command, Stdio},
    };

    #[test]
    fn request_ids_are_restricted() {
        assert!(validate_request("550e8400-e29b-41d4-a716-446655440000").is_ok());
        assert!(validate_request("../other-process").is_err());
        assert!(validate_request("").is_err());
        assert!(is_cancellable("scan"));
        assert!(is_cancellable("measurement-analyze"));
        assert!(!is_cancellable("apply"));
        assert!(!COMMANDS.contains(&"measurement-watchdog"));
        assert!(!COMMANDS.iter().any(|command| command.contains("script")));
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn cancelling_fake_agent_terminates_its_blocking_subprocess() {
        let mut fake_agent = Command::new("powershell.exe")
            .args([
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "$child = Start-Process ping.exe -ArgumentList '-t','127.0.0.1' -WindowStyle Hidden -PassThru; [Console]::Out.WriteLine($child.Id); [Console]::Out.Flush(); $child.WaitForExit()",
            ])
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .creation_flags(0x08000000)
            .spawn()
            .expect("fake agent should start");
        let child_pid: u32 = BufReader::new(
            fake_agent
                .stdout
                .take()
                .expect("fake agent stdout should exist"),
        )
        .lines()
        .next()
        .expect("fake agent should report child readiness")
        .expect("fake agent child PID should be readable")
        .parse()
        .expect("fake agent child PID should be numeric");

        let state = AgentState::default();
        state
            .0
            .lock()
            .expect("test agent state should lock")
            .insert(
                "fake-scan".into(),
                ActiveAgent {
                    process_id: fake_agent.id(),
                    cancellable: true,
                    cancelled: false,
                },
            );
        let cancelled = cancel_request(&state.0, "fake-scan");
        if cancelled.is_err() {
            let _ = fake_agent.kill();
            let _ = Command::new("taskkill.exe")
                .args(["/PID", &child_pid.to_string(), "/F"])
                .creation_flags(0x08000000)
                .status();
        }
        assert_eq!(cancelled, Ok(true));
        let _ = fake_agent.wait();

        assert!(
            wait_for_process_exit(child_pid),
            "blocking subprocess was orphaned"
        );
    }

    #[cfg(target_os = "windows")]
    fn process_exists(process_id: u32) -> bool {
        let filter = format!("PID eq {process_id}");
        let output = Command::new("tasklist.exe")
            .args(["/FI", &filter, "/FO", "CSV", "/NH"])
            .creation_flags(0x08000000)
            .output()
            .expect("tasklist should run");
        let csv = String::from_utf8_lossy(&output.stdout);
        csv.lines()
            .any(|line| line.split(',').nth(1) == Some(&format!("\"{process_id}\"")))
    }

    #[cfg(target_os = "windows")]
    fn wait_for_process_exit(process_id: u32) -> bool {
        for _ in 0..40 {
            if !process_exists(process_id) {
                return true;
            }
            std::thread::sleep(std::time::Duration::from_millis(50));
        }
        false
    }
}

fn agent_path(app: &AppHandle) -> Result<PathBuf, String> {
    if cfg!(debug_assertions) {
        return Ok(PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("../../src/NeuroTune.Agent/bin/Release/net8.0-windows/NeuroTune.Agent.exe"));
    }
    app.path()
        .resource_dir()
        .map(|path| path.join("agent/NeuroTune.Agent.exe"))
        .map_err(|error| error.to_string())
}

#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .setup(|app| {
            if cfg!(debug_assertions) {
                app.handle().plugin(
                    tauri_plugin_log::Builder::default()
                        .level(log::LevelFilter::Info)
                        .build(),
                )?;
            }
            Ok(())
        })
        .manage(AgentState::default())
        .invoke_handler(tauri::generate_handler![agent, cancel_agent])
        .run(tauri::generate_context!())
        .expect("error while running NeuroTune");
}
