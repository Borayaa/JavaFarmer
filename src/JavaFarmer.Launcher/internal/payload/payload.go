// Package payload defines the data that scripts/build-exe.sh appends to the launcher:
//
//	[launcher PE image][zip archive][trailer]
//
// The zip holds the staged game folder plus a manifest (ManifestName) that lists every file
// with its size and SHA-256. The trailer is the last TrailerSize bytes of the exe:
//
//	magic (8) | zip offset (uint64 LE) | zip length (uint64 LE) | SHA-256 of the zip (32)
package payload

import (
	"archive/zip"
	"bytes"
	"compress/flate"
	"crypto/sha256"
	"encoding/binary"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path"
	"path/filepath"
	"sort"
	"strings"
	"time"
)

// Magic identifies a JavaFarmer payload trailer.
const Magic = "JFPAYLD1"

// TrailerSize is the size of the encoded trailer in bytes.
const TrailerSize = 8 + 8 + 8 + sha256.Size

// ManifestName is the zip entry that holds the manifest. It is never extracted.
const ManifestName = ".javafarmer/manifest.json"

// ManifestVersion is the manifest format version.
const ManifestVersion = 1

// Trailer locates the payload zip inside the exe.
type Trailer struct {
	Offset int64
	Length int64
	SHA256 [sha256.Size]byte
}

// ID is the short payload id used as the cache folder name (first 12 hex chars of the SHA-256).
func (t Trailer) ID() string { return hex.EncodeToString(t.SHA256[:])[:12] }

// Encode returns the binary trailer.
func (t Trailer) Encode() []byte {
	b := make([]byte, TrailerSize)
	copy(b, Magic)
	binary.LittleEndian.PutUint64(b[8:], uint64(t.Offset))
	binary.LittleEndian.PutUint64(b[16:], uint64(t.Length))
	copy(b[24:], t.SHA256[:])
	return b
}

// ReadTrailer reads and checks the trailer at the end of a file of the given size.
func ReadTrailer(r io.ReaderAt, size int64) (Trailer, error) {
	var t Trailer
	if size < TrailerSize {
		return t, errors.New("no payload: file too small")
	}
	b := make([]byte, TrailerSize)
	if _, err := r.ReadAt(b, size-TrailerSize); err != nil {
		return t, fmt.Errorf("read trailer: %w", err)
	}
	if string(b[:8]) != Magic {
		return t, errors.New("no payload: trailer magic not found (the exe is incomplete or was not built by build-exe.sh)")
	}
	off := binary.LittleEndian.Uint64(b[8:])
	n := binary.LittleEndian.Uint64(b[16:])
	if off > uint64(size) || n > uint64(size) || off+n != uint64(size-TrailerSize) {
		return t, fmt.Errorf("bad trailer: offset %d + length %d does not end at %d", off, n, size-TrailerSize)
	}
	t.Offset, t.Length = int64(off), int64(n)
	copy(t.SHA256[:], b[24:])
	return t, nil
}

// File is one manifest entry. Path uses forward slashes and is relative to the game folder.
type File struct {
	Path   string `json:"path"`
	Size   int64  `json:"size"`
	SHA256 string `json:"sha256"`
}

// Manifest lists the files of the game folder.
type Manifest struct {
	Version int    `json:"version"`
	Name    string `json:"name"`
	Release string `json:"release"`
	Exe     string `json:"exe"`
	Files   []File `json:"files"`
}

// ValidPath reports whether p is a clean, relative, forward-slash path that stays inside the game folder.
func ValidPath(p string) bool {
	if p == "" || strings.ContainsAny(p, "\\:\x00") || strings.HasPrefix(p, "/") {
		return false
	}
	if path.Clean(p) != p || p == "." || p == ".." || strings.HasPrefix(p, "../") {
		return false
	}
	for _, seg := range strings.Split(p, "/") {
		// Windows ignores trailing dots and spaces in names: reject them so two names cannot map to one file.
		if seg == "" || seg == "." || seg == ".." || strings.HasSuffix(seg, ".") || strings.HasSuffix(seg, " ") {
			return false
		}
	}
	return !strings.HasPrefix(strings.ToLower(p), ".javafarmer/")
}

// Validate checks the manifest structure.
func (m *Manifest) Validate() error {
	if m.Version != ManifestVersion {
		return fmt.Errorf("manifest version %d is not supported", m.Version)
	}
	if !ValidPath(m.Exe) || strings.Contains(m.Exe, "/") || !strings.EqualFold(path.Ext(m.Exe), ".exe") {
		return fmt.Errorf("manifest: bad exe %q", m.Exe)
	}
	seen := make(map[string]bool, len(m.Files))
	hasExe := false
	for _, f := range m.Files {
		if !ValidPath(f.Path) {
			return fmt.Errorf("manifest: bad path %q", f.Path)
		}
		k := Key(f.Path)
		if seen[k] {
			return fmt.Errorf("manifest: duplicate path %q", f.Path)
		}
		seen[k] = true
		if f.Size < 0 {
			return fmt.Errorf("manifest: bad size for %q", f.Path)
		}
		if b, err := hex.DecodeString(f.SHA256); err != nil || len(b) != sha256.Size {
			return fmt.Errorf("manifest: bad sha256 for %q", f.Path)
		}
		if f.Path == m.Exe {
			hasExe = true
		}
	}
	if !hasExe {
		return fmt.Errorf("manifest: exe %q is not in the file list", m.Exe)
	}
	return nil
}

// Key is the case-insensitive lookup key of a path (Windows file names are case-insensitive).
func Key(p string) string { return strings.ToLower(p) }

// IsExecutable reports whether a file is a PE image Windows can load (.exe/.dll).
func IsExecutable(p string) bool {
	switch strings.ToLower(path.Ext(p)) {
	case ".exe", ".dll":
		return true
	}
	return false
}

// IsCode reports whether a file is checked by SHA-256 on every launch: PE files, config files
// (.ini, .json, .cfg) and everything under BepInEx/. Other files are checked by size.
func IsCode(p string) bool {
	if IsExecutable(p) {
		return true
	}
	switch strings.ToLower(path.Ext(p)) {
	case ".ini", ".json", ".cfg":
		return true
	}
	return strings.HasPrefix(Key(p), "bepinex/")
}

// strictDirs hold only shipped code: any file there that is not in the manifest is removed.
var strictDirs = []string{"bepinex/core/", "bepinex/plugins/", "bepinex/patchers/", "monobleedingedge/", "thefarmerwasreplaced_data/managed/"}

// keepDirs hold runtime data that is never removed (BepInEx config and caches).
var keepDirs = []string{"bepinex/config/", "bepinex/cache/"}

// ShouldRemoveUnexpected reports whether a file that is not in the manifest must be deleted:
// executables anywhere, and any file inside a code-only folder.
func ShouldRemoveUnexpected(p string) bool {
	if IsExecutable(p) {
		return true
	}
	k := Key(p)
	for _, d := range keepDirs {
		if strings.HasPrefix(k, d) {
			return false
		}
	}
	for _, d := range strictDirs {
		if strings.HasPrefix(k, d) {
			return true
		}
	}
	return false
}

// LoadManifest reads and validates the manifest from the payload zip and checks that every
// listed file has a zip entry of the right size. It returns the manifest and the entries by path.
func LoadManifest(zr *zip.Reader) (*Manifest, map[string]*zip.File, error) {
	entries := make(map[string]*zip.File, len(zr.File))
	var mf *zip.File
	for _, f := range zr.File {
		if f.Name == ManifestName {
			mf = f
			continue
		}
		entries[f.Name] = f
	}
	if mf == nil {
		return nil, nil, errors.New("payload has no manifest")
	}
	rc, err := mf.Open()
	if err != nil {
		return nil, nil, err
	}
	defer rc.Close()
	var m Manifest
	dec := json.NewDecoder(io.LimitReader(rc, 64<<20))
	dec.DisallowUnknownFields()
	if err := dec.Decode(&m); err != nil {
		return nil, nil, fmt.Errorf("manifest: %w", err)
	}
	if err := m.Validate(); err != nil {
		return nil, nil, err
	}
	for _, f := range m.Files {
		e, ok := entries[f.Path]
		if !ok {
			return nil, nil, fmt.Errorf("payload is missing %q", f.Path)
		}
		if int64(e.UncompressedSize64) != f.Size {
			return nil, nil, fmt.Errorf("payload entry %q has the wrong size", f.Path)
		}
	}
	return &m, entries, nil
}

// HashFile returns the hex SHA-256 and size of a file.
func HashFile(p string) (string, int64, error) {
	f, err := os.Open(p)
	if err != nil {
		return "", 0, err
	}
	defer f.Close()
	h := sha256.New()
	buf := make([]byte, 1<<20)
	n, err := io.CopyBuffer(h, f, buf)
	if err != nil {
		return "", 0, err
	}
	return hex.EncodeToString(h.Sum(nil)), n, nil
}

// BuildOptions configures BuildZip.
type BuildOptions struct {
	Name    string
	Release string
	Exe     string    // game exe, relative to the stage folder
	ModTime time.Time // timestamp written for every entry (fixed, for reproducible builds)
}

// BuildZip writes a deterministic zip of the stage folder (sorted entries, fixed times, deflate)
// with the manifest as the first entry. Symlinks and special files are rejected.
func BuildZip(w io.Writer, stage string, opt BuildOptions) (*Manifest, error) {
	m := &Manifest{Version: ManifestVersion, Name: opt.Name, Release: opt.Release, Exe: opt.Exe}
	err := filepath.WalkDir(stage, func(p string, d fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if d.IsDir() {
			return nil
		}
		if !d.Type().IsRegular() {
			return fmt.Errorf("%s: not a regular file", p)
		}
		rel, err := filepath.Rel(stage, p)
		if err != nil {
			return err
		}
		rel = filepath.ToSlash(rel)
		if !ValidPath(rel) {
			return fmt.Errorf("%s: path not allowed in the payload", rel)
		}
		sum, n, err := HashFile(p)
		if err != nil {
			return err
		}
		m.Files = append(m.Files, File{Path: rel, Size: n, SHA256: sum})
		return nil
	})
	if err != nil {
		return nil, err
	}
	sort.Slice(m.Files, func(i, j int) bool { return m.Files[i].Path < m.Files[j].Path })
	if err := m.Validate(); err != nil {
		return nil, err
	}
	mj, err := json.MarshalIndent(m, "", " ")
	if err != nil {
		return nil, err
	}

	zw := zip.NewWriter(w)
	zw.RegisterCompressor(zip.Deflate, func(out io.Writer) (io.WriteCloser, error) {
		return flate.NewWriter(out, flate.BestCompression)
	})
	add := func(name string, r io.Reader) error {
		hdr := &zip.FileHeader{Name: name, Method: zip.Deflate, Modified: opt.ModTime.UTC()}
		hdr.SetMode(0o644)
		fw, err := zw.CreateHeader(hdr)
		if err != nil {
			return err
		}
		_, err = io.Copy(fw, r)
		return err
	}
	if err := add(ManifestName, bytes.NewReader(mj)); err != nil {
		return nil, err
	}
	for _, f := range m.Files {
		fh, err := os.Open(filepath.Join(stage, filepath.FromSlash(f.Path)))
		if err != nil {
			return nil, err
		}
		err = add(f.Path, fh)
		fh.Close()
		if err != nil {
			return nil, fmt.Errorf("%s: %w", f.Path, err)
		}
	}
	if err := zw.Close(); err != nil {
		return nil, err
	}
	return m, nil
}
