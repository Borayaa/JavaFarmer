package main

import (
	"archive/zip"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"regexp"
	"runtime"
	"sort"
	"strings"
	"sync"
	"time"

	"javafarmer/launcher/internal/payload"
)

// Source is the payload appended to the launcher exe.
type Source struct {
	file     *os.File
	trailer  payload.Trailer
	zr       *zip.Reader
	manifest *payload.Manifest
	entries  map[string]*zip.File
}

// OpenSource opens the payload at the end of the given exe.
func OpenSource(exe string) (*Source, error) {
	f, err := os.Open(exe)
	if err != nil {
		return nil, err
	}
	st, err := f.Stat()
	if err != nil {
		f.Close()
		return nil, err
	}
	t, err := payload.ReadTrailer(f, st.Size())
	if err != nil {
		f.Close()
		return nil, err
	}
	zr, err := zip.NewReader(io.NewSectionReader(f, t.Offset, t.Length), t.Length)
	if err != nil {
		f.Close()
		return nil, fmt.Errorf("payload zip: %w", err)
	}
	m, entries, err := payload.LoadManifest(zr)
	if err != nil {
		f.Close()
		return nil, err
	}
	return &Source{file: f, trailer: t, zr: zr, manifest: m, entries: entries}, nil
}

// Close releases the exe.
func (s *Source) Close() error { return s.file.Close() }

// VerifyPayload checks the SHA-256 of the whole payload against the trailer.
func (s *Source) VerifyPayload() error {
	h := sha256.New()
	if _, err := io.CopyBuffer(h, io.NewSectionReader(s.file, s.trailer.Offset, s.trailer.Length), make([]byte, 1<<20)); err != nil {
		return fmt.Errorf("read payload: %w", err)
	}
	if string(h.Sum(nil)) != string(s.trailer.SHA256[:]) {
		return errors.New("the payload is corrupted (SHA-256 mismatch): download JavaFarmer.exe again")
	}
	return nil
}

// extract writes one manifest file from the payload to dst (via a temp file and rename) and
// checks its SHA-256 on the way.
func (s *Source) extract(f payload.File, dst string) error {
	e := s.entries[f.Path]
	rc, err := e.Open()
	if err != nil {
		return err
	}
	defer rc.Close()
	if err := os.MkdirAll(filepath.Dir(dst), 0o755); err != nil {
		return err
	}
	tmp := dst + tmpSuffix
	out, err := os.OpenFile(tmp, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, 0o644)
	if err != nil {
		return err
	}
	h := sha256.New()
	n, err := io.CopyBuffer(io.MultiWriter(out, h), rc, make([]byte, 256<<10))
	if cerr := out.Close(); err == nil {
		err = cerr
	}
	if err == nil && (n != f.Size || hex.EncodeToString(h.Sum(nil)) != f.SHA256) {
		err = errors.New("content does not match the manifest")
	}
	if err != nil {
		os.Remove(tmp)
		return fmt.Errorf("extract %s: %w", f.Path, err)
	}
	if st, lerr := os.Lstat(dst); lerr == nil {
		if st.IsDir() {
			if err := os.RemoveAll(dst); err != nil {
				os.Remove(tmp)
				return err
			}
		} else {
			os.Chmod(dst, 0o644) // a read-only file cannot be replaced on Windows
		}
	}
	if err := os.Rename(tmp, dst); err != nil {
		os.Remove(tmp)
		return fmt.Errorf("install %s: %w", f.Path, err)
	}
	return nil
}

const tmpSuffix = ".jf-tmp"

// Stats describes what Prepare did.
type Stats struct {
	Dir       string
	Extracted bool // a fresh extraction was done
	Checked   int  // files checked
	Hashed    int  // files checked by SHA-256
	Restored  []string
	Removed   []string
	Duration  time.Duration
}

var (
	versionDirRe = regexp.MustCompile(`^[0-9a-f]{12}$`)
	tmpDirRe     = regexp.MustCompile(`^\.tmp-[0-9a-f]{12}-[0-9]+$`)
)

// Prepare makes sure base/<payload id> holds an exact copy of the payload and returns its path.
// The caller must hold the base lock.
func Prepare(base string, src *Source, logf func(string, ...any)) (*Stats, error) {
	start := time.Now()
	id := src.trailer.ID()
	dir := filepath.Join(base, id)
	st := &Stats{Dir: dir}
	if fi, err := os.Lstat(dir); err != nil || !fi.IsDir() {
		if err == nil {
			if err := os.RemoveAll(dir); err != nil {
				return nil, err
			}
		}
		logf("first run of payload %s: extracting %d files", id, len(src.manifest.Files))
		if err := extractFresh(base, id, src, logf); err != nil {
			return nil, err
		}
		st.Extracted = true
		st.Checked = len(src.manifest.Files)
		st.Hashed = st.Checked
	} else if err := verifyDir(dir, src, st, logf); err != nil {
		return nil, err
	}
	removeOldVersions(base, id, logf)
	st.Duration = time.Since(start)
	return st, nil
}

func extractFresh(base, id string, src *Source, logf func(string, ...any)) error {
	if err := src.VerifyPayload(); err != nil {
		return err
	}
	tmp := filepath.Join(base, fmt.Sprintf(".tmp-%s-%d", id, os.Getpid()))
	os.RemoveAll(tmp)
	if err := os.MkdirAll(tmp, 0o755); err != nil {
		return err
	}
	ok := false
	defer func() {
		if !ok {
			os.RemoveAll(tmp)
		}
	}()
	files := sortedBySize(src.manifest.Files)
	if err := parallel(files, func(f payload.File) error {
		return src.extract(f, filepath.Join(tmp, filepath.FromSlash(f.Path)))
	}); err != nil {
		return err
	}
	migrateConfig(base, id, tmp, logf)
	if err := os.Rename(tmp, filepath.Join(base, id)); err != nil {
		return fmt.Errorf("move the unpacked game into place: %w", err)
	}
	ok = true
	return nil
}

// verifyDir checks every manifest file (SHA-256 for code/config, size for the rest), restores
// the broken ones from the payload and removes unexpected executables.
func verifyDir(dir string, src *Source, st *Stats, logf func(string, ...any)) error {
	m := src.manifest
	known := make(map[string]bool, len(m.Files))
	var toHash, broken []payload.File
	for _, f := range m.Files {
		known[payload.Key(f.Path)] = true
		fi, err := os.Lstat(filepath.Join(dir, filepath.FromSlash(f.Path)))
		st.Checked++
		switch {
		case err != nil || !fi.Mode().IsRegular() || fi.Size() != f.Size:
			broken = append(broken, f)
		case payload.IsCode(f.Path):
			toHash = append(toHash, f)
		}
	}
	st.Hashed = len(toHash)
	var mu sync.Mutex
	err := parallel(sortedBySize(toHash), func(f payload.File) error {
		sum, _, err := payload.HashFile(filepath.Join(dir, filepath.FromSlash(f.Path)))
		if err != nil || sum != f.SHA256 {
			mu.Lock()
			broken = append(broken, f)
			mu.Unlock()
		}
		return nil
	})
	if err != nil {
		return err
	}

	// Unexpected files: executables anywhere, anything inside code-only folders, our temp files.
	err = filepath.WalkDir(dir, func(p string, d fs.DirEntry, err error) error {
		if err != nil {
			return nil // unreadable entries are left alone; manifest files were checked above
		}
		if p == dir {
			return nil
		}
		rel, rerr := filepath.Rel(dir, p)
		if rerr != nil {
			return nil
		}
		rel = filepath.ToSlash(rel)
		if d.IsDir() {
			if d.Type()&fs.ModeSymlink != 0 {
				return fs.SkipDir
			}
			return nil
		}
		if known[payload.Key(rel)] {
			return nil
		}
		if payload.ShouldRemoveUnexpected(rel) || strings.HasSuffix(rel, tmpSuffix) || d.Type()&fs.ModeSymlink != 0 {
			os.Chmod(p, 0o644)
			if rmErr := os.Remove(p); rmErr != nil {
				logf("could not remove unexpected file %s: %v", rel, rmErr)
				if payload.IsExecutable(rel) {
					return fmt.Errorf("could not remove the unexpected file %s: %w", rel, rmErr)
				}
				return nil
			}
			st.Removed = append(st.Removed, rel)
		}
		return nil
	})
	if err != nil {
		return err
	}

	sort.Slice(broken, func(i, j int) bool { return broken[i].Path < broken[j].Path })
	if err := parallel(broken, func(f payload.File) error {
		return src.extract(f, filepath.Join(dir, filepath.FromSlash(f.Path)))
	}); err != nil {
		return err
	}
	for _, f := range broken {
		st.Restored = append(st.Restored, f.Path)
	}
	return nil
}

// migrateConfig copies BepInEx/config/*.cfg from the newest older version into the new folder,
// so settings such as Enabled=false survive an update.
func migrateConfig(base, id, into string, logf func(string, ...any)) {
	ents, err := os.ReadDir(base)
	if err != nil {
		return
	}
	var best string
	var bestTime time.Time
	for _, e := range ents {
		if !e.IsDir() || e.Name() == id || !versionDirRe.MatchString(e.Name()) {
			continue
		}
		fi, err := os.Stat(filepath.Join(base, e.Name(), "BepInEx", "config"))
		if err == nil && fi.IsDir() && fi.ModTime().After(bestTime) {
			best, bestTime = e.Name(), fi.ModTime()
		}
	}
	if best == "" {
		return
	}
	from := filepath.Join(base, best, "BepInEx", "config")
	cfgs, _ := os.ReadDir(from)
	for _, c := range cfgs {
		if !c.Type().IsRegular() || !strings.EqualFold(filepath.Ext(c.Name()), ".cfg") {
			continue
		}
		data, err := os.ReadFile(filepath.Join(from, c.Name()))
		if err != nil {
			continue
		}
		dst := filepath.Join(into, "BepInEx", "config", c.Name())
		if os.MkdirAll(filepath.Dir(dst), 0o755) == nil && os.WriteFile(dst, data, 0o644) == nil {
			logf("kept setting file %s from %s", c.Name(), best)
		}
	}
}

// removeOldVersions deletes other JavaFarmer version folders and stale temp folders in base.
func removeOldVersions(base, id string, logf func(string, ...any)) {
	ents, err := os.ReadDir(base)
	if err != nil {
		return
	}
	for _, e := range ents {
		n := e.Name()
		if !e.IsDir() || n == id || !(versionDirRe.MatchString(n) || tmpDirRe.MatchString(n)) {
			continue
		}
		if err := os.RemoveAll(filepath.Join(base, n)); err != nil {
			logf("could not remove old folder %s: %v", n, err)
		} else {
			logf("removed old folder %s", n)
		}
	}
}

func sortedBySize(files []payload.File) []payload.File {
	out := append([]payload.File(nil), files...)
	sort.SliceStable(out, func(i, j int) bool { return out[i].Size > out[j].Size })
	return out
}

// parallel runs fn for every file on a small worker pool and returns the first error.
func parallel(files []payload.File, fn func(payload.File) error) error {
	workers := runtime.NumCPU()
	if workers > 8 {
		workers = 8
	}
	if workers < 2 {
		workers = 2
	}
	ch := make(chan payload.File)
	var wg sync.WaitGroup
	var once sync.Once
	var first error
	stop := make(chan struct{})
	for i := 0; i < workers; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for f := range ch {
				if err := fn(f); err != nil {
					once.Do(func() { first = err; close(stop) })
				}
			}
		}()
	}
loop:
	for _, f := range files {
		select {
		case ch <- f:
		case <-stop:
			break loop
		}
	}
	close(ch)
	wg.Wait()
	return first
}
