//go:build unix

package main

import (
	"fmt"
	"os"
	"path/filepath"
	"syscall"
	"time"
)

func showError(title, msg string) { fmt.Fprintf(os.Stderr, "%s: %s\n", title, msg) }

func setWineDLLOverride(string, func(string, ...any)) error { return nil }

func lockBase(base string, timeout time.Duration) (func(), error) {
	f, err := os.OpenFile(filepath.Join(base, ".lock"), os.O_CREATE|os.O_RDWR, 0o644)
	if err != nil {
		return nil, err
	}
	deadline := time.Now().Add(timeout)
	for {
		err := syscall.Flock(int(f.Fd()), syscall.LOCK_EX|syscall.LOCK_NB)
		if err == nil {
			return func() { syscall.Flock(int(f.Fd()), syscall.LOCK_UN); f.Close() }, nil
		}
		if time.Now().After(deadline) {
			f.Close()
			return nil, err
		}
		time.Sleep(200 * time.Millisecond)
	}
}
