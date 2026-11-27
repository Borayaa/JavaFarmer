// jfres copies the application icon out of a Windows exe into an .ico file, so the launcher
// can show the game's icon. Used by scripts/build-exe.sh.
//
//	jfres -exe TheFarmerWasReplaced.exe -ico icon.ico
package main

import (
	"bytes"
	"debug/pe"
	"encoding/binary"
	"errors"
	"flag"
	"fmt"
	"os"
	"sort"
)

const (
	rtIcon      = 3
	rtGroupIcon = 14
)

func main() {
	exe := flag.String("exe", "", "source exe")
	ico := flag.String("ico", "", "output .ico")
	flag.Parse()
	if *exe == "" || *ico == "" {
		flag.Usage()
		os.Exit(2)
	}
	data, err := extractIcon(*exe)
	if err == nil {
		err = os.WriteFile(*ico, data, 0o644)
	}
	if err != nil {
		fmt.Fprintln(os.Stderr, "jfres:", err)
		os.Exit(1)
	}
}

type rsrc struct {
	data []byte // whole .rsrc section
	va   uint32 // its virtual address
}

func (r *rsrc) u16(off uint32) uint16 { return binary.LittleEndian.Uint16(r.data[off:]) }
func (r *rsrc) u32(off uint32) uint32 { return binary.LittleEndian.Uint32(r.data[off:]) }

type entry struct {
	id     uint32
	offset uint32
	isDir  bool
}

// dir lists the id entries of the resource directory at off (named entries are skipped).
func (r *rsrc) dir(off uint32) ([]entry, error) {
	if int(off)+16 > len(r.data) {
		return nil, errors.New("resource directory out of range")
	}
	named, ids := uint32(r.u16(off+12)), uint32(r.u16(off+14))
	var out []entry
	for i := uint32(0); i < named+ids; i++ {
		e := off + 16 + i*8
		if int(e)+8 > len(r.data) {
			return nil, errors.New("resource entry out of range")
		}
		name, to := r.u32(e), r.u32(e+4)
		if name&0x80000000 != 0 {
			continue
		}
		out = append(out, entry{id: name, offset: to &^ 0x80000000, isDir: to&0x80000000 != 0})
	}
	sort.Slice(out, func(i, j int) bool { return out[i].id < out[j].id })
	return out, nil
}

// first returns the data of the first language of resource (type, id); id 0 means the first id.
func (r *rsrc) first(typ, id uint32) ([]byte, error) {
	types, err := r.dir(0)
	if err != nil {
		return nil, err
	}
	for _, t := range types {
		if t.id != typ || !t.isDir {
			continue
		}
		names, err := r.dir(t.offset)
		if err != nil {
			return nil, err
		}
		for _, n := range names {
			if (id != 0 && n.id != id) || !n.isDir {
				continue
			}
			langs, err := r.dir(n.offset)
			if err != nil || len(langs) == 0 {
				return nil, fmt.Errorf("resource %d/%d has no data", typ, n.id)
			}
			de := langs[0].offset
			if int(de)+16 > len(r.data) {
				return nil, errors.New("data entry out of range")
			}
			rva, size := r.u32(de), r.u32(de+4)
			start := int64(rva) - int64(r.va)
			if start < 0 || start+int64(size) > int64(len(r.data)) {
				return nil, errors.New("resource data out of range")
			}
			return r.data[start : start+int64(size)], nil
		}
	}
	return nil, fmt.Errorf("resource type %d id %d not found", typ, id)
}

func extractIcon(exe string) ([]byte, error) {
	f, err := pe.Open(exe)
	if err != nil {
		return nil, err
	}
	defer f.Close()
	var dirRVA uint32
	switch oh := f.OptionalHeader.(type) {
	case *pe.OptionalHeader64:
		dirRVA = oh.DataDirectory[pe.IMAGE_DIRECTORY_ENTRY_RESOURCE].VirtualAddress
	case *pe.OptionalHeader32:
		dirRVA = oh.DataDirectory[pe.IMAGE_DIRECTORY_ENTRY_RESOURCE].VirtualAddress
	}
	if dirRVA == 0 {
		return nil, errors.New("the exe has no resources")
	}
	var r *rsrc
	for _, s := range f.Sections {
		if dirRVA >= s.VirtualAddress && dirRVA < s.VirtualAddress+s.VirtualSize {
			d, err := s.Data()
			if err != nil {
				return nil, err
			}
			off := dirRVA - s.VirtualAddress
			r = &rsrc{data: d[off:], va: dirRVA}
			break
		}
	}
	if r == nil {
		return nil, errors.New("resource section not found")
	}
	grp, err := r.first(rtGroupIcon, 0)
	if err != nil {
		return nil, err
	}
	if len(grp) < 6 {
		return nil, errors.New("icon group too short")
	}
	count := int(binary.LittleEndian.Uint16(grp[4:]))
	if len(grp) < 6+count*14 {
		return nil, errors.New("icon group truncated")
	}
	var head, body bytes.Buffer
	head.Write(grp[:6])
	offset := uint32(6 + count*16)
	for i := 0; i < count; i++ {
		e := grp[6+i*14 : 6+(i+1)*14]
		img, err := r.first(rtIcon, uint32(binary.LittleEndian.Uint16(e[12:])))
		if err != nil {
			return nil, err
		}
		head.Write(e[:8]) // width, height, colours, reserved, planes, bit count
		binary.Write(&head, binary.LittleEndian, uint32(len(img)))
		binary.Write(&head, binary.LittleEndian, offset)
		body.Write(img)
		offset += uint32(len(img))
	}
	return append(head.Bytes(), body.Bytes()...), nil
}
