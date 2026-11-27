package main

import (
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"time"
)

const maxLogSize = 1 << 20

// logger appends to launcher.log in the base folder. It never fails: without a log file it discards.
type logger struct {
	mu   sync.Mutex
	f    *os.File
	path string
}

func openLog(base string) *logger {
	p := filepath.Join(base, "launcher.log")
	flags := os.O_CREATE | os.O_WRONLY | os.O_APPEND
	if fi, err := os.Stat(p); err == nil && fi.Size() > maxLogSize {
		flags |= os.O_TRUNC
	}
	f, _ := os.OpenFile(p, flags, 0o644)
	return &logger{f: f, path: p}
}

func (l *logger) Printf(format string, a ...any) {
	l.mu.Lock()
	defer l.mu.Unlock()
	if l.f != nil {
		fmt.Fprintf(l.f, "%s [%d] %s\n", time.Now().Format("2006-01-02 15:04:05.000"), os.Getpid(), fmt.Sprintf(format, a...))
	}
}

func (l *logger) Close() {
	if l.f != nil {
		l.f.Close()
	}
}
