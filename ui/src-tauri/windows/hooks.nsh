; Remove the sign-in task (LOGON_TASK in src/lib.rs) on a real uninstall; updates keep the user's choice.
!macro NSIS_HOOK_PREUNINSTALL
  ${If} $UpdateMode <> 1
    nsExec::Exec '"$SYSDIR\schtasks.exe" /Delete /TN "NeuroTune kept changes check" /F'
    Pop $0
  ${EndIf}
!macroend
