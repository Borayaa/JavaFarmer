// jfpack builds JavaFarmer.exe: it zips a staged game folder (with a manifest) and appends
// the zip and a trailer to the launcher exe. Used by scripts/build-exe.sh.
//
//	jfpack -stage DIR -launcher launcher.exe -out JavaFarmer.exe [-exe TheFarmerWasReplaced.exe] [-release 0.1.0]
package main

import (
	"bufio"
	"crypto/sha256"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"time"

	"javafarmer/launcher/internal/payload"
)

func main() {
	stage := flag.String("stage", "", "staged game folder")
	launcher := flag.String("launcher", "", "launcher exe (built without payload)")
	out := flag.String("out", "", "output exe")
	gameExe := flag.String("exe", "TheFarmerWasReplaced.exe", "game exe, relative to the stage folder")
	release := flag.String("release", "0.0.0", "release name written to the manifest")
	epoch := flag.Int64("mtime", 1577836800, "timestamp (unix) for all zip entries")
	flag.Parse()
	if *stage == "" || *launcher == "" || *out == "" {
		flag.Usage()
		os.Exit(2)
	}
	if err := pack(*stage, *launcher, *out, *gameExe, *release, time.Unix(*epoch, 0)); err != nil {
		fmt.Fprintln(os.Stderr, "jfpack:", err)
		os.Exit(1)
	}
}

func pack(stage, launcher, out, gameExe, release string, mtime time.Time) error {
	lb, err := os.ReadFile(launcher)
	if err != nil {
		return err
	}
	if len(lb) >= payload.TrailerSize && string(lb[len(lb)-payload.TrailerSize:][:8]) == payload.Magic {
		return fmt.Errorf("%s already has a payload", launcher)
	}
	tmp := out + ".tmp"
	f, err := os.Create(tmp)
	if err != nil {
		return err
	}
	defer os.Remove(tmp)
	if _, err := f.Write(lb); err != nil {
		f.Close()
		return err
	}
	h := sha256.New()
	cw := &countWriter{}
	bw := bufio.NewWriterSize(io.MultiWriter(f, h, cw), 1<<20)
	m, err := payload.BuildZip(bw, stage, payload.BuildOptions{Name: "JavaFarmer", Release: release, Exe: gameExe, ModTime: mtime})
	if err == nil {
		err = bw.Flush()
	}
	if err != nil {
		f.Close()
		return err
	}
	t := payload.Trailer{Offset: int64(len(lb)), Length: cw.n}
	copy(t.SHA256[:], h.Sum(nil))
	if _, err := f.Write(t.Encode()); err != nil {
		f.Close()
		return err
	}
	if err := f.Close(); err != nil {
		return err
	}
	if err := os.Rename(tmp, out); err != nil {
		return err
	}
	var total int64
	for _, x := range m.Files {
		total += x.Size
	}
	fmt.Printf("jfpack: %d files, %.1f MB unpacked, payload %.1f MB, id %s -> %s\n",
		len(m.Files), float64(total)/1e6, float64(cw.n)/1e6, t.ID(), filepath.Base(out))
	return nil
}

type countWriter struct{ n int64 }

func (c *countWriter) Write(p []byte) (int, error) { c.n += int64(len(p)); return len(p), nil }
