// JavaFarmer launcher: one Windows exe that carries the whole modded game.
//
// Unity cannot run from inside an exe, so on the first start the launcher unpacks the game
// (appended to this exe as a zip, see internal/payload) into %LOCALAPPDATA%\JavaFarmer\<id>\.
// Every start it checks the unpacked files against the embedded manifest, repairs what differs,
// removes unexpected executables, and then starts the game with a clean environment.
// It reads no file other than its own exe and its cache folder.
package main

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

// prepareOnlyFlag unpacks and checks the game, then exits without starting it (for tests and troubleshooting).
const prepareOnlyFlag = "--javafarmer-prepare-only"

func main() {
	os.Exit(run())
}

func run() int {
	base, err := baseDir()
	if err != nil {
		fail(nil, "Cannot find the local app data folder: %v", err)
		return 1
	}
	if err := os.MkdirAll(base, 0o755); err != nil {
		fail(nil, "Cannot create %s: %v", base, err)
		return 1
	}
	lg := openLog(base)
	defer lg.Close()
	logf := lg.Printf
	logf("launcher start, args=%q", os.Args[1:])

	exe, err := os.Executable()
	if err != nil {
		fail(lg, "Cannot find the launcher exe: %v", err)
		return 1
	}
	src, err := OpenSource(exe)
	if err != nil {
		fail(lg, "Cannot read the game data inside %s:\n%v", exe, err)
		return 1
	}
	defer src.Close()

	unlock, err := lockBase(base, 2*time.Minute)
	if err != nil {
		fail(lg, "Another JavaFarmer launcher is busy: %v", err)
		return 1
	}
	st, err := Prepare(base, src, logf)
	unlock()
	if err != nil {
		fail(lg, "Cannot prepare the game files in %s:\n%v", base, err)
		return 1
	}
	logf("payload %s ready in %s: extracted=%v checked=%d hashed=%d restored=%d removed=%d (%s)",
		src.trailer.ID(), st.Dir, st.Extracted, st.Checked, st.Hashed, len(st.Restored), len(st.Removed), st.Duration.Round(time.Millisecond))
	for _, p := range st.Restored {
		logf("  restored %s", p)
	}
	for _, p := range st.Removed {
		logf("  removed unexpected %s", p)
	}

	args, prepareOnly, dropped := sanitizeArgs(os.Args[1:])
	if len(dropped) > 0 {
		logf("ignored arguments %q (Doorstop overrides are not allowed)", dropped)
	}
	if prepareOnly {
		logf("%s given: not starting the game", prepareOnlyFlag)
		return 0
	}
	game := filepath.Join(st.Dir, filepath.FromSlash(src.manifest.Exe))
	src.Close() // do not hold the launcher exe open while the game runs
	if err := setWineDLLOverride(game, logf); err != nil {
		// not fatal: the game still runs, only without the mod
		logf("WARNING: could not set the Wine DLL override for winhttp (BepInEx may not load): %v", err)
	}
	code, err := launch(game, st.Dir, args, sanitizeEnv(os.Environ()))
	if err != nil {
		fail(lg, "Cannot start the game (%s):\n%v", game, err)
		return 1
	}
	logf("game exited with code %d", code)
	return code
}

// launch starts the game and waits for it. It returns the game's exit code.
func launch(exe, dir string, args, env []string) (int, error) {
	cmd := exec.Command(exe, args...)
	cmd.Dir = dir
	cmd.Env = env
	// A GUI-subsystem exe usually has no standard handles: only pass the ones that exist.
	if os.Stdin != nil {
		cmd.Stdin = os.Stdin
	}
	if os.Stdout != nil {
		cmd.Stdout = os.Stdout
	}
	if os.Stderr != nil {
		cmd.Stderr = os.Stderr
	}
	err := cmd.Run()
	var ee *exec.ExitError
	if errors.As(err, &ee) {
		return ee.ExitCode(), nil
	}
	if err != nil {
		return 0, err
	}
	return cmd.ProcessState.ExitCode(), nil
}

// baseDir is %LOCALAPPDATA%\JavaFarmer on Windows (os.UserCacheDir elsewhere, for development).
func baseDir() (string, error) {
	d, err := os.UserCacheDir()
	if err != nil {
		return "", err
	}
	if !filepath.IsAbs(d) {
		return "", fmt.Errorf("%q is not an absolute path", d)
	}
	return filepath.Join(d, "JavaFarmer"), nil
}

// sanitizeArgs drops the launcher's own flag and Doorstop command-line overrides
// (--doorstop-<option> [value]), which could make the game load another assembly.
func sanitizeArgs(in []string) (out []string, prepareOnly bool, dropped []string) {
	for i := 0; i < len(in); i++ {
		a := in[i]
		if a == prepareOnlyFlag {
			prepareOnly = true
			continue
		}
		if strings.HasPrefix(strings.ToLower(a), "--doorstop-") {
			dropped = append(dropped, a)
			if !strings.Contains(a, "=") && i+1 < len(in) && !strings.HasPrefix(in[i+1], "-") {
				i++
				dropped = append(dropped, in[i])
			}
			continue
		}
		out = append(out, a)
	}
	return out, prepareOnly, dropped
}

// sanitizeEnv removes DOORSTOP_* variables and adds winhttp=n,b to WINEDLLOVERRIDES (ignored on
// real Windows). Note: current Wine builds the WINE* variables of a new process from the Unix
// environment, so under Wine/Proton the override that counts is the registry one (setWineDLLOverride).
func sanitizeEnv(in []string) []string {
	out := make([]string, 0, len(in)+1)
	overrides := ""
	for _, kv := range in {
		k, v, _ := strings.Cut(kv, "=")
		if k == "" { // Windows per-drive variables like "=C:=C:\"
			out = append(out, kv)
			continue
		}
		switch {
		case strings.HasPrefix(strings.ToUpper(k), "DOORSTOP_"):
			continue
		case strings.EqualFold(k, "WINEDLLOVERRIDES"):
			overrides = v
			continue
		}
		out = append(out, kv)
	}
	return append(out, "WINEDLLOVERRIDES="+mergeDLLOverrides(overrides, "winhttp", "n,b"))
}

// mergeDLLOverrides removes dll from an existing WINEDLLOVERRIDES value ("a,b=n;c=d") and adds dll=mode.
func mergeDLLOverrides(existing, dll, mode string) string {
	var parts []string
	for _, entry := range strings.Split(existing, ";") {
		entry = strings.TrimSpace(entry)
		if entry == "" {
			continue
		}
		names, m, hasMode := strings.Cut(entry, "=")
		var keep []string
		for _, n := range strings.Split(names, ",") {
			n = strings.TrimSpace(n)
			bare := strings.TrimSuffix(strings.ToLower(n), ".dll")
			if n != "" && bare != dll {
				keep = append(keep, n)
			}
		}
		if len(keep) == 0 {
			continue
		}
		e := strings.Join(keep, ",")
		if hasMode {
			e += "=" + m
		}
		parts = append(parts, e)
	}
	return strings.Join(append(parts, dll+"="+mode), ";")
}

func fail(lg *logger, format string, a ...any) {
	msg := fmt.Sprintf(format, a...)
	if lg != nil {
		lg.Printf("ERROR: %s", msg)
		msg += "\n\nLog: " + lg.path
	}
	showError("JavaFarmer", msg)
}
