//go:build windows

package main

import (
	"errors"
	"os"
	"path/filepath"
	"syscall"
	"time"
	"unsafe"
)

var (
	user32             = syscall.NewLazyDLL("user32.dll")
	procMessageBox     = user32.NewProc("MessageBoxW")
	advapi32           = syscall.NewLazyDLL("advapi32.dll")
	procRegCreateKeyEx = advapi32.NewProc("RegCreateKeyExW")
	procRegSetValueEx  = advapi32.NewProc("RegSetValueExW")
	procWineVersion    = syscall.NewLazyDLL("ntdll.dll").NewProc("wine_get_version")
)

// isWine reports whether the launcher runs under Wine/Proton (ntdll exports wine_get_version).
func isWine() bool { return procWineVersion.Find() == nil }

// setWineDLLOverride makes Wine/Proton load the game's own winhttp.dll (the Doorstop proxy that starts
// BepInEx) instead of Wine's builtin one, for this game exe only:
// HKCU\Software\Wine\AppDefaults\<exe>\DllOverrides  winhttp = native,builtin.
// Wine takes WINEDLLOVERRIDES for a new process from the Unix environment, not from the environment the
// launcher passes, so the registry is the way to set it from inside the prefix. Real Windows is left alone.
func setWineDLLOverride(gameExe string, logf func(string, ...any)) error {
	if !isWine() {
		return nil
	}
	key, err := syscall.UTF16PtrFromString(`Software\Wine\AppDefaults\` + filepath.Base(gameExe) + `\DllOverrides`)
	if err != nil {
		return err
	}
	var h syscall.Handle
	r, _, _ := procRegCreateKeyEx.Call(uintptr(syscall.HKEY_CURRENT_USER), uintptr(unsafe.Pointer(key)), 0, 0, 0,
		uintptr(syscall.KEY_SET_VALUE), 0, uintptr(unsafe.Pointer(&h)), 0)
	if r != 0 {
		return &os.SyscallError{Syscall: "RegCreateKeyEx", Err: syscall.Errno(r)}
	}
	defer syscall.RegCloseKey(h)
	name, _ := syscall.UTF16PtrFromString("winhttp")
	data, _ := syscall.UTF16FromString("native,builtin")
	r, _, _ = procRegSetValueEx.Call(uintptr(h), uintptr(unsafe.Pointer(name)), 0, uintptr(syscall.REG_SZ),
		uintptr(unsafe.Pointer(&data[0])), uintptr(len(data)*2))
	if r != 0 {
		return &os.SyscallError{Syscall: "RegSetValueEx", Err: syscall.Errno(r)}
	}
	logf("Wine detected: DLL override winhttp=native,builtin set for %s", filepath.Base(gameExe))
	return nil
}

const (
	mbOK        = 0x00000000
	mbIconError = 0x00000010
	mbSetFg     = 0x00010000
)

func showError(title, msg string) {
	t, _ := syscall.UTF16PtrFromString(title)
	m, _ := syscall.UTF16PtrFromString(msg)
	procMessageBox.Call(0, uintptr(unsafe.Pointer(m)), uintptr(unsafe.Pointer(t)), mbOK|mbIconError|mbSetFg)
}

const errorSharingViolation syscall.Errno = 32

// lockBase opens base\.lock with no sharing, so only one launcher prepares the cache at a time.
func lockBase(base string, timeout time.Duration) (func(), error) {
	p, err := syscall.UTF16PtrFromString(filepath.Join(base, ".lock"))
	if err != nil {
		return nil, err
	}
	deadline := time.Now().Add(timeout)
	for {
		h, err := syscall.CreateFile(p, syscall.GENERIC_READ|syscall.GENERIC_WRITE, 0, nil,
			syscall.OPEN_ALWAYS, syscall.FILE_ATTRIBUTE_NORMAL, 0)
		if err == nil {
			return func() { syscall.CloseHandle(h) }, nil
		}
		if !errors.Is(err, errorSharingViolation) || time.Now().After(deadline) {
			return nil, &os.PathError{Op: "lock", Path: filepath.Join(base, ".lock"), Err: err}
		}
		time.Sleep(200 * time.Millisecond)
	}
}
