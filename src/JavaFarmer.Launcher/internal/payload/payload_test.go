package payload

import (
	"archive/zip"
	"bytes"
	"crypto/sha256"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestTrailerRoundTrip(t *testing.T) {
	data := append(bytes.Repeat([]byte{'x'}, 100), bytes.Repeat([]byte{'z'}, 50)...)
	tr := Trailer{Offset: 100, Length: 50, SHA256: sha256.Sum256(data[100:])}
	file := append(data, tr.Encode()...)
	got, err := ReadTrailer(bytes.NewReader(file), int64(len(file)))
	if err != nil {
		t.Fatal(err)
	}
	if got != tr {
		t.Fatalf("got %+v, want %+v", got, tr)
	}
	if len(tr.ID()) != 12 {
		t.Fatalf("bad id %q", tr.ID())
	}
}

func TestTrailerRejectsBadInput(t *testing.T) {
	if _, err := ReadTrailer(bytes.NewReader([]byte("short")), 5); err == nil {
		t.Error("short file accepted")
	}
	plain := bytes.Repeat([]byte{0}, 200)
	if _, err := ReadTrailer(bytes.NewReader(plain), 200); err == nil {
		t.Error("file without magic accepted")
	}
	bad := append(bytes.Repeat([]byte{0}, 100), Trailer{Offset: 10, Length: 50}.Encode()...)
	if _, err := ReadTrailer(bytes.NewReader(bad), int64(len(bad))); err == nil {
		t.Error("trailer whose payload does not end at the trailer accepted")
	}
	huge := append(bytes.Repeat([]byte{0}, 100), Trailer{Offset: 1 << 62, Length: 1 << 62}.Encode()...)
	if _, err := ReadTrailer(bytes.NewReader(huge), int64(len(huge))); err == nil {
		t.Error("overflowing trailer accepted")
	}
}

func TestValidPath(t *testing.T) {
	good := []string{"a.exe", "BepInEx/core/BepInEx.dll", ".doorstop_version", "x/y z/w.txt"}
	bad := []string{"", "/abs", "../x", "a/../b", "a//b", "a/./b", "a\\b", "C:x", "a/", "x.", "x /y", ".javafarmer/manifest.json", "..", "."}
	for _, p := range good {
		if !ValidPath(p) {
			t.Errorf("ValidPath(%q) = false", p)
		}
	}
	for _, p := range bad {
		if ValidPath(p) {
			t.Errorf("ValidPath(%q) = true", p)
		}
	}
}

func TestClassification(t *testing.T) {
	code := []string{"UnityPlayer.dll", "X.EXE", "doorstop_config.ini", "a/b.json", "BepInEx/x.cfg", "BepInEx/plugins/JavaFarmer/content/docs.md", "BepInEx/plugins/JavaFarmer/content/java/docs/home.md", "BepInEx/plugins/JavaFarmer/content/python/Strings/extensions.txt"}
	for _, p := range code {
		if !IsCode(p) {
			t.Errorf("IsCode(%q) = false", p)
		}
	}
	for _, p := range []string{"TheFarmerWasReplaced_Data/resources.assets", ".doorstop_version"} {
		if IsCode(p) {
			t.Errorf("IsCode(%q) = true", p)
		}
	}
	remove := []string{"version.dll", "BepInEx/config/evil.dll", "BepInEx/plugins/x.txt", "BepInEx/patchers/p.dll.config", "TheFarmerWasReplaced_Data/Managed/x.mdb", "MonoBleedingEdge/etc/mono/config2"}
	keep := []string{"BepInEx/LogOutput.log", "BepInEx/config/BepInEx.cfg", "BepInEx/cache/chainloader_typeloader.dat", "output_log.txt", "TheFarmerWasReplaced_Data/foo.txt"}
	for _, p := range remove {
		if !ShouldRemoveUnexpected(p) {
			t.Errorf("ShouldRemoveUnexpected(%q) = false", p)
		}
	}
	for _, p := range keep {
		if ShouldRemoveUnexpected(p) {
			t.Errorf("ShouldRemoveUnexpected(%q) = true", p)
		}
	}
}

func writeTree(t *testing.T, dir string, files map[string]string) {
	t.Helper()
	for p, c := range files {
		full := filepath.Join(dir, filepath.FromSlash(p))
		if err := os.MkdirAll(filepath.Dir(full), 0o755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(full, []byte(c), 0o644); err != nil {
			t.Fatal(err)
		}
	}
}

func TestBuildZipDeterministicAndLoadable(t *testing.T) {
	stage := t.TempDir()
	writeTree(t, stage, map[string]string{
		"Game.exe":           "MZ game",
		"Game_Data/a.bin":    "aaaa",
		"BepInEx/core/b.dll": "MZ bep",
	})
	opt := BuildOptions{Name: "t", Release: "1", Exe: "Game.exe", ModTime: time.Unix(1577836800, 0)}
	var b1, b2 bytes.Buffer
	m, err := BuildZip(&b1, stage, opt)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := BuildZip(&b2, stage, opt); err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(b1.Bytes(), b2.Bytes()) {
		t.Fatal("BuildZip is not deterministic")
	}
	if len(m.Files) != 3 || m.Files[0].Path != "BepInEx/core/b.dll" {
		t.Fatalf("unexpected manifest %+v", m.Files)
	}
	zr, err := zip.NewReader(bytes.NewReader(b1.Bytes()), int64(b1.Len()))
	if err != nil {
		t.Fatal(err)
	}
	if zr.File[0].Name != ManifestName {
		t.Fatalf("manifest is not the first entry: %s", zr.File[0].Name)
	}
	m2, entries, err := LoadManifest(zr)
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 3 || m2.Exe != "Game.exe" || m2.Files[2].SHA256 != m.Files[2].SHA256 {
		t.Fatalf("round trip mismatch: %+v", m2)
	}
}

func TestBuildZipNeedsExe(t *testing.T) {
	stage := t.TempDir()
	writeTree(t, stage, map[string]string{"x.txt": "x"})
	if _, err := BuildZip(&bytes.Buffer{}, stage, BuildOptions{Exe: "Game.exe"}); err == nil {
		t.Fatal("stage without the game exe accepted")
	}
}

func TestValidateRejects(t *testing.T) {
	h := "0000000000000000000000000000000000000000000000000000000000000000"
	cases := []Manifest{
		{Version: 2, Exe: "a.exe", Files: []File{{Path: "a.exe", SHA256: h}}},
		{Version: 1, Exe: "a.exe", Files: []File{{Path: "../a.exe", SHA256: h}}},
		{Version: 1, Exe: "a.exe", Files: []File{{Path: "a.exe", SHA256: h}, {Path: "A.EXE", SHA256: h}}},
		{Version: 1, Exe: "a.exe", Files: []File{{Path: "a.exe", SHA256: "zz"}}},
		{Version: 1, Exe: "sub/a.exe", Files: []File{{Path: "sub/a.exe", SHA256: h}}},
		{Version: 1, Exe: "a.exe", Files: []File{{Path: "b.exe", SHA256: h}}},
	}
	for i, m := range cases {
		if err := m.Validate(); err == nil {
			t.Errorf("case %d accepted", i)
		}
	}
}
