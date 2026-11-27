//go:build unix

package main

import (
	"bytes"
	"crypto/sha256"
	"os"
	"path/filepath"
	"reflect"
	"slices"
	"strings"
	"testing"
	"time"

	"javafarmer/launcher/internal/payload"
)

var stageFiles = map[string]string{
	"Game.exe":                         "MZ game exe",
	"Game_Data/resources.assets":       strings.Repeat("asset", 1000),
	"Game_Data/Managed/Core.dll":       "MZ core",
	"BepInEx/core/BepInEx.dll":         "MZ bepinex",
	"BepInEx/plugins/Mod/Mod.dll":      "MZ mod",
	"BepInEx/plugins/Mod/content/a.md": "docs",
	"doorstop_config.ini":              "[General]\nenabled=true\n",
	".doorstop_version":                "4.6.0",
}

// buildExe writes a fake launcher exe with a payload built from stageFiles (plus extra) and returns its path.
func buildExe(t *testing.T, extra map[string]string) string {
	t.Helper()
	stage := t.TempDir()
	files := map[string]string{}
	for k, v := range stageFiles {
		files[k] = v
	}
	for k, v := range extra {
		files[k] = v
	}
	for p, c := range files {
		full := filepath.Join(stage, filepath.FromSlash(p))
		os.MkdirAll(filepath.Dir(full), 0o755)
		if err := os.WriteFile(full, []byte(c), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	var zipBuf bytes.Buffer
	if _, err := payload.BuildZip(&zipBuf, stage, payload.BuildOptions{Exe: "Game.exe", ModTime: time.Unix(0, 0)}); err != nil {
		t.Fatal(err)
	}
	head := []byte("MZ fake launcher image")
	tr := payload.Trailer{Offset: int64(len(head)), Length: int64(zipBuf.Len()), SHA256: sha256.Sum256(zipBuf.Bytes())}
	exe := filepath.Join(t.TempDir(), "JavaFarmer.exe")
	data := append(append(head, zipBuf.Bytes()...), tr.Encode()...)
	if err := os.WriteFile(exe, data, 0o755); err != nil {
		t.Fatal(err)
	}
	return exe
}

func open(t *testing.T, exe string) *Source {
	t.Helper()
	src, err := OpenSource(exe)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { src.Close() })
	return src
}

func nolog(string, ...any) {}

func assertTree(t *testing.T, dir string, files map[string]string) {
	t.Helper()
	for p, c := range files {
		got, err := os.ReadFile(filepath.Join(dir, filepath.FromSlash(p)))
		if err != nil || string(got) != c {
			t.Errorf("%s: content differs (err=%v)", p, err)
		}
	}
}

func TestPrepareExtractsThenVerifies(t *testing.T) {
	base := t.TempDir()
	src := open(t, buildExe(t, nil))
	st, err := Prepare(base, src, nolog)
	if err != nil {
		t.Fatal(err)
	}
	if !st.Extracted || st.Dir != filepath.Join(base, src.trailer.ID()) {
		t.Fatalf("unexpected stats %+v", st)
	}
	assertTree(t, st.Dir, stageFiles)
	if _, err := os.Stat(filepath.Join(st.Dir, filepath.FromSlash(payload.ManifestName))); err == nil {
		t.Error("manifest was extracted")
	}
	st2, err := Prepare(base, src, nolog)
	if err != nil {
		t.Fatal(err)
	}
	if st2.Extracted || len(st2.Restored) != 0 || len(st2.Removed) != 0 || st2.Checked != len(stageFiles) {
		t.Fatalf("second run changed something: %+v", st2)
	}
	// code files are hashed, the asset and .doorstop_version only size-checked
	if st2.Hashed != len(stageFiles)-2 {
		t.Fatalf("hashed %d files, want %d", st2.Hashed, len(stageFiles)-2)
	}
}

func TestPrepareRepairsTampering(t *testing.T) {
	base := t.TempDir()
	src := open(t, buildExe(t, nil))
	st, err := Prepare(base, src, nolog)
	if err != nil {
		t.Fatal(err)
	}
	d := st.Dir
	w := func(p, c string) {
		full := filepath.Join(d, filepath.FromSlash(p))
		os.MkdirAll(filepath.Dir(full), 0o755)
		if err := os.WriteFile(full, []byte(c), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	w("BepInEx/plugins/Mod/Mod.dll", "MZ evl")                // same size, other content: needs the hash check
	w("Game_Data/resources.assets", "short")                  // size change of a non-code file
	os.Remove(filepath.Join(d, "Game_Data/Managed/Core.dll")) // missing
	os.Chmod(filepath.Join(d, "doorstop_config.ini"), 0o444)
	w("doorstop_config.ini.x", "") // non-executable stray at the root: kept
	os.Chmod(filepath.Join(d, "doorstop_config.ini"), 0o644)
	w("doorstop_config.ini", "[General]\nenabled=fals\n")
	w("version.dll", "MZ injected")                  // stray executable at the root
	w("BepInEx/plugins/Other/Other.dll", "MZ other") // stray plugin
	w("BepInEx/plugins/Mod/notes.txt", "x")          // any file in a code-only folder
	w("BepInEx/config/evil.DLL", "MZ")               // executables are removed even from config
	w("BepInEx/LogOutput.log", "log")                // runtime files are kept
	w("BepInEx/config/BepInEx.cfg", "cfg")
	w("BepInEx/cache/chainloader_typeloader.dat", "c")
	w("Game_Data/x.jf-tmp", "leftover")

	st2, err := Prepare(base, src, nolog)
	if err != nil {
		t.Fatal(err)
	}
	assertTree(t, d, stageFiles)
	wantRestored := []string{"BepInEx/plugins/Mod/Mod.dll", "Game_Data/Managed/Core.dll", "Game_Data/resources.assets", "doorstop_config.ini"}
	if !reflect.DeepEqual(st2.Restored, wantRestored) {
		t.Errorf("restored %v, want %v", st2.Restored, wantRestored)
	}
	slices.Sort(st2.Removed)
	wantRemoved := []string{"BepInEx/config/evil.DLL", "BepInEx/plugins/Mod/notes.txt", "BepInEx/plugins/Other/Other.dll", "Game_Data/x.jf-tmp", "version.dll"}
	if !reflect.DeepEqual(st2.Removed, wantRemoved) {
		t.Errorf("removed %v, want %v", st2.Removed, wantRemoved)
	}
	for _, p := range []string{"BepInEx/LogOutput.log", "BepInEx/config/BepInEx.cfg", "BepInEx/cache/chainloader_typeloader.dat", "doorstop_config.ini.x"} {
		if _, err := os.Stat(filepath.Join(d, p)); err != nil {
			t.Errorf("%s was removed", p)
		}
	}
	st3, err := Prepare(base, src, nolog)
	if err != nil || len(st3.Restored)+len(st3.Removed) != 0 {
		t.Fatalf("third run not clean: %+v %v", st3, err)
	}
}

func TestPrepareReplacesDirectoryWithFile(t *testing.T) {
	base := t.TempDir()
	src := open(t, buildExe(t, nil))
	st, _ := Prepare(base, src, nolog)
	p := filepath.Join(st.Dir, "BepInEx/core/BepInEx.dll")
	os.Remove(p)
	os.MkdirAll(filepath.Join(p, "sub"), 0o755)
	if _, err := Prepare(base, src, nolog); err != nil {
		t.Fatal(err)
	}
	assertTree(t, st.Dir, stageFiles)
}

func TestNewVersionReplacesOldAndKeepsConfig(t *testing.T) {
	base := t.TempDir()
	old := open(t, buildExe(t, nil))
	st, err := Prepare(base, old, nolog)
	if err != nil {
		t.Fatal(err)
	}
	os.MkdirAll(filepath.Join(st.Dir, "BepInEx/config"), 0o755)
	os.WriteFile(filepath.Join(st.Dir, "BepInEx/config/javafarmer.plugin.cfg"), []byte("Enabled = false"), 0o644)
	os.WriteFile(filepath.Join(st.Dir, "BepInEx/config/x.dll"), []byte("MZ"), 0o644)
	os.MkdirAll(filepath.Join(base, "NotOurs"), 0o755)
	os.MkdirAll(filepath.Join(base, ".tmp-0123456789ab-42"), 0o755)

	cur := open(t, buildExe(t, map[string]string{"Game_Data/new.txt": "v2"}))
	st2, err := Prepare(base, cur, nolog)
	if err != nil {
		t.Fatal(err)
	}
	if st2.Dir == st.Dir || !st2.Extracted {
		t.Fatalf("new payload did not get a new folder: %+v", st2)
	}
	if _, err := os.Stat(st.Dir); !os.IsNotExist(err) {
		t.Error("old version folder was not removed")
	}
	if _, err := os.Stat(filepath.Join(base, ".tmp-0123456789ab-42")); !os.IsNotExist(err) {
		t.Error("stale temp folder was not removed")
	}
	if _, err := os.Stat(filepath.Join(base, "NotOurs")); err != nil {
		t.Error("a folder that is not ours was removed")
	}
	got, _ := os.ReadFile(filepath.Join(st2.Dir, "BepInEx/config/javafarmer.plugin.cfg"))
	if string(got) != "Enabled = false" {
		t.Errorf("config not migrated: %q", got)
	}
	if _, err := os.Stat(filepath.Join(st2.Dir, "BepInEx/config/x.dll")); err == nil {
		t.Error("a non-.cfg file was migrated")
	}
}

func TestCorruptPayloadIsRejected(t *testing.T) {
	exe := buildExe(t, nil)
	data, _ := os.ReadFile(exe)
	// the last trailer byte is part of the payload SHA-256: the zip still opens, the hash check must fail
	data[len(data)-1] ^= 0xff
	os.WriteFile(exe, data, 0o755)
	src := open(t, exe)
	base := t.TempDir()
	if _, err := Prepare(base, src, nolog); err == nil || !strings.Contains(err.Error(), "corrupted") {
		t.Fatalf("corrupted payload accepted: %v", err)
	}
	if ents, _ := os.ReadDir(base); len(ents) != 0 {
		t.Errorf("a failed extraction left %d entries behind", len(ents))
	}
}

func TestExeWithoutPayload(t *testing.T) {
	exe := filepath.Join(t.TempDir(), "plain.exe")
	os.WriteFile(exe, bytes.Repeat([]byte("MZ"), 100), 0o755)
	if _, err := OpenSource(exe); err == nil {
		t.Fatal("exe without payload accepted")
	}
}

func TestMergeDLLOverrides(t *testing.T) {
	cases := map[string]string{
		"":                                  "winhttp=n,b",
		"d3d11=n":                           "d3d11=n;winhttp=n,b",
		"winhttp=b":                         "winhttp=n,b",
		"dxgi,WinHttp.dll,d3d11=n;mscoree=": "dxgi,d3d11=n;mscoree=;winhttp=n,b",
		"steam.exe=b;dxgi=n":                "steam.exe=b;dxgi=n;winhttp=n,b",
	}
	for in, want := range cases {
		if got := mergeDLLOverrides(in, "winhttp", "n,b"); got != want {
			t.Errorf("merge(%q) = %q, want %q", in, got, want)
		}
	}
}

func TestSanitizeEnv(t *testing.T) {
	env := sanitizeEnv([]string{"PATH=/bin", "DOORSTOP_ENABLED=0", "doorstop_target_assembly=x.dll", "WINEDLLOVERRIDES=dxgi=n", "=C:=C:\\"})
	want := []string{"PATH=/bin", "=C:=C:\\", "WINEDLLOVERRIDES=dxgi=n;winhttp=n,b"}
	if !reflect.DeepEqual(env, want) {
		t.Fatalf("got %q, want %q", env, want)
	}
}

func TestSanitizeArgs(t *testing.T) {
	out, prep, dropped := sanitizeArgs([]string{"-screen-width", "800", "--doorstop-target-assembly", "evil.dll", "--DOORSTOP-ENABLED=false", "--doorstop-enabled", "-popupwindow", prepareOnlyFlag})
	if !prep {
		t.Error("prepare-only flag not seen")
	}
	if !reflect.DeepEqual(out, []string{"-screen-width", "800", "-popupwindow"}) {
		t.Errorf("args %q", out)
	}
	if len(dropped) != 4 {
		t.Errorf("dropped %q", dropped)
	}
}

func TestLaunchPassesExitCodeArgsDirAndEnv(t *testing.T) {
	dir := t.TempDir()
	code, err := launch("/bin/sh", dir, []string{"-c", `[ "$(pwd)" = "$1" ] && [ "$JF" = ok ] && exit 7; exit 1`, "sh", dir}, []string{"JF=ok"})
	if err != nil {
		t.Fatal(err)
	}
	if code != 7 {
		t.Fatalf("exit code %d, want 7", code)
	}
	if _, err := launch(filepath.Join(dir, "missing.exe"), dir, nil, nil); err == nil {
		t.Fatal("missing exe did not fail")
	}
}
