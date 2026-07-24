use serde_json::Value;
use std::{
    io::Write,
    path::PathBuf,
    process::{Command, Stdio},
};
use tauri::{AppHandle, Manager};

const COMMANDS: &[&str] = &[
    "get-state",
    "provider-defaults",
    "save-provider",
    "oauth-openrouter",
    "models",
    "scan",
    "diagnose",
    "actions",
    "apply",
    "history",
    "rollback",
];

#[tauri::command]
async fn agent(app: AppHandle, command: String, payload: Option<Value>) -> Result<Value, String> {
    if !COMMANDS.contains(&command.as_str()) {
        return Err("Unsupported NeuroTune agent command".into());
    }
    tauri::async_runtime::spawn_blocking(move || run_agent(&app, &command, payload))
        .await
        .map_err(|error| error.to_string())?
}

fn run_agent(app: &AppHandle, command: &str, payload: Option<Value>) -> Result<Value, String> {
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

    if let Some(mut stdin) = child.stdin.take() {
        stdin
            .write_all(payload.unwrap_or(Value::Null).to_string().as_bytes())
            .map_err(|error| format!("Could not send the agent request: {error}"))?;
    }
    let output = child
        .wait_with_output()
        .map_err(|error| error.to_string())?;
    let response: Value = serde_json::from_slice(&output.stdout).map_err(|_| {
        let stderr = String::from_utf8_lossy(&output.stderr);
        format!("The NeuroTune agent returned an invalid response. {stderr}")
    })?;
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
        .invoke_handler(tauri::generate_handler![agent])
        .run(tauri::generate_context!())
        .expect("error while running NeuroTune");
}
