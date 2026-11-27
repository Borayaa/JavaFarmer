using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace JavaFarmer.Compiler.CFamily
{
    /// <summary>
    /// farm.h (C23) and farm.hpp (C++23): the API stub files. Header-only, real code: the game functions are stubs that
    /// do nothing outside the game, the collections / strings / printing helpers really work, so JavaFarmer programs
    /// build and run with gcc / g++ (the conformance and differential tests do exactly that).
    /// </summary>
    internal static class FarmStub
    {
        /// <summary>Constant members written into the stub (game data; the compiler itself accepts any member).</summary>
        internal static readonly Dictionary<string, List<string>> Known = new Dictionary<string, List<string>>
        {
            ["Entities"] = new List<string> { "Apple", "Bush", "Cactus", "Carrot", "Dead_Pumpkin", "Dinosaur", "Grass", "Hedge", "Pumpkin", "Sunflower", "Treasure", "Tree" },
            ["Items"] = new List<string> { "Bone", "Cactus", "Carrot", "Fertilizer", "Gold", "Hay", "Piggy", "Power", "Pumpkin", "Water", "Weird_Substance", "Wood" },
            ["Grounds"] = new List<string> { "Grassland", "Soil" },
            ["Hats"] = new List<string> { "Brown_Hat", "Cactus_Hat", "Carrot_Hat", "Dinosaur_Hat", "Gold_Hat", "Gold_Trophy_Hat", "Golden_Cactus_Hat", "Golden_Carrot_Hat", "Golden_Gold_Hat", "Golden_Pumpkin_Hat", "Golden_Sunflower_Hat", "Golden_Tree_Hat", "Gray_Hat", "Green_Hat", "Pumpkin_Hat", "Purple_Hat", "Silver_Trophy_Hat", "Straw_Hat", "Sunflower_Hat", "The_Farmers_Remains", "Top_Hat", "Traffic_Cone", "Traffic_Cone_Stack", "Tree_Hat", "Wizard_Hat", "Wood_Trophy_Hat" },
            ["Leaderboards"] = new List<string> { "Cactus", "Cactus_Single", "Carrots", "Carrots_Single", "Dinosaur", "Fastest_Reset", "Hay", "Hay_Single", "Maze", "Maze_Single", "Pumpkins", "Pumpkins_Single", "Sunflowers", "Sunflowers_Single", "Wood", "Wood_Single" },
            ["Unlocks"] = new List<string> { "Auto_Unlock", "Cactus", "Carrots", "Costs", "Debug", "Debug_2", "Dictionaries", "Dinosaurs", "Expand", "Fertilizer", "Functions", "Grass", "Hats", "Imports", "Leaderboard", "Lists", "Loops", "Mazes", "Megafarm", "Multi_Trade", "Operators", "Plant", "Polyculture", "Pumpkins", "Senses", "Simulation", "Speed", "Sunflowers", "Timing", "Trees", "Utilities", "Variables", "Watering", "The_Farmers_Remains" },
        };

        static readonly string[] Handles = { "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction" };

        static Dictionary<string, List<string>> Members(IEnumerable<string> sources, string sep)
        {
            var res = new Dictionary<string, List<string>>();
            List<string> known;
            lock (Known) foreach (var kv in Known) res[kv.Key] = kv.Value.ToList();
            if (sources != null)
            {
                var all = string.Join("\n", sources);
                foreach (var bag in GameApi.ConstantCategories)
                {
                    if (!res.TryGetValue(bag, out known)) res[bag] = known = new List<string>();
                    foreach (Match m in Regex.Matches(all, @"\b" + bag + Regex.Escape(sep) + @"([A-Za-z_]\w*)"))
                        if (!known.Contains(m.Groups[1].Value)) known.Add(m.Groups[1].Value);
                }
            }
            foreach (var bag in GameApi.ConstantCategories) if (!res.ContainsKey(bag)) res[bag] = new List<string>();
            return res;
        }

        // ================================================================== C

        static readonly string[] CElems = { "int", "double", "bool", "string", "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position" };
        static readonly string[] CKeys = { "int", "double", "string", "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position" };

        static string CTypeOf(IrType t, string text)
        {
            if (text != null && text.Contains("|")) return "JfObj";
            if (t == null) return "double";
            switch (t.Kind)
            {
                case IrTypeKind.Void: return "void";
                case IrTypeKind.Int: return "int";
                case IrTypeKind.Float: return "double";
                case IrTypeKind.Bool: return "bool";
                case IrTypeKind.String: return "const char *";
                case IrTypeKind.Game: return t.GameName;
                case IrTypeKind.List: case IrTypeKind.Array: return "List(" + CElemOf(t.Elem) + ")";
                case IrTypeKind.Set: return "Set(" + CElemOf(t.Elem) + ")";
                case IrTypeKind.Map: return "Map(" + CElemOf(t.Key) + ", " + CElemOf(t.Elem) + ")";
            }
            return "double";
        }

        static string CElemOf(IrType t)
        {
            if (t == null) return "double";
            switch (t.Kind)
            {
                case IrTypeKind.Int: return "int";
                case IrTypeKind.Float: return "double";
                case IrTypeKind.Bool: return "bool";
                case IrTypeKind.String: return "string";
                case IrTypeKind.Game: return t.GameName == "Companion" ? "Position" : t.GameName;
            }
            return "double";
        }

        static string Zero(string ct)
        {
            switch (ct)
            {
                case "void": return null;
                case "int": case "double": return "0";
                case "bool": return "false";
                case "Position": return "(Position){0, 0}";
            }
            return "NULL";
        }

        public static string CHeader(IEnumerable<string> sources)
        {
            var members = Members(sources, ".");
            var sb = new StringBuilder();
            sb.Append(@"// farm.h - the game API of The Farmer Was Replaced for C (C23), written by the JavaFarmer mod.
// #include ""farm.h"" in every code window. The game never compiles this file: it documents the API for
// external editors and makes JavaFarmer programs real C. Outside the game the farm functions do nothing,
// while the collections (List / Set / Map), the string helpers and quick_print really work, so
//     gcc -std=c23 -Wall main.c -lm
// builds and runs a program that only computes and prints.
// Game values are handles (NULL = none). Constants keep the game's names: Entities.Carrot, Items.Hay, North ...
// Collections: List(T) / Set(T) / Map(K, V) for T = int, double, bool, string, Position and the game types:
//     List(int) xs = list_of(int, 1, 2, 3);  list_add(xs, 4);  list_get(xs, 0);  for_each (int x, xs) { ... }
#ifndef JAVAFARMER_FARM_H
#define JAVAFARMER_FARM_H

#include <stdarg.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

typedef const char *string;
struct jf_obj { const char *name; };
typedef const struct jf_obj *JfObj;
");
            foreach (var h in Handles) sb.Append("typedef const struct jf_obj *").Append(h).Append(";\n");
            sb.Append(@"struct jf_drone { int id; };
typedef const struct jf_drone *Drone;
typedef struct { int x; int y; } Position;
struct jf_companion { Entity entity; Position position; int x; int y; };
typedef const struct jf_companion *Companion;

[[noreturn]] static inline void jf_fail(const char *what) { fprintf(stderr, ""farm.h: %s\n"", what); exit(1); }

// ---------------------------------------------------------------- constants
");
            foreach (var d in GameApi.DirectionNames)
                sb.Append("static const struct jf_obj jf_dir_").Append(d).Append(" = {\"").Append(d).Append("\"};\n#define ").Append(d).Append(" (&jf_dir_").Append(d).Append(")\n");
            foreach (var bag in GameApi.ConstantCategories)
            {
                var type = GameApi.GameTypeOfCategory(bag);
                var names = members[bag];
                if (names.Count == 0) names = new List<string> { "None_" };
                foreach (var m in names) sb.Append("static const struct jf_obj jf_").Append(bag).Append('_').Append(m).Append(" = {\"").Append(bag).Append('.').Append(m).Append("\"};\n");
                sb.Append("static const struct {");
                foreach (var m in names) sb.Append(' ').Append(type).Append(' ').Append(m).Append(';');
                sb.Append(" } ").Append(bag).Append(" = {");
                sb.Append(string.Join(",", names.Select(m => " &jf_" + bag + "_" + m)));
                sb.Append(" };\n");
            }
            sb.Append(CText);
            // collections
            foreach (var e in CElems) sb.Append("JF_LIST(").Append(e).Append(")\nJF_SET(").Append(e).Append(")\n");
            foreach (var k in CKeys) foreach (var v in CElems) sb.Append("JF_MAP(").Append(k).Append(", ").Append(v).Append(")\n");
            sb.Append(CGeneric());
            sb.Append(CApi());
            sb.Append("\n#endif\n");
            return sb.ToString();
        }

        static string Assoc(IEnumerable<string> types, Func<string, string> fn) => string.Join(", ", types.Select(t => t + ": " + fn(t)));

        static string CGeneric()
        {
            var sb = new StringBuilder("\n// ---------------------------------------------------------------- generic collection functions\n");
            var lists = CElems.Select(e => "List_" + e).ToList();
            var sets = CElems.Select(e => "Set_" + e).ToList();
            var maps = new List<(string T, string K, string V)>();
            foreach (var k in CKeys) foreach (var v in CElems) maps.Add(("Map_" + k + "_" + v, k, v));
            sb.Append("#define List(T) List_##T\n#define Set(T) Set_##T\n#define Map(K, V) Map_##K##_##V\n");
            sb.Append("#define list_new(T) jf_list_new_##T()\n#define set_new(T) jf_set_new_##T()\n#define map_new(K, V) jf_map_new_##K##_##V()\n");
            sb.Append("#define list_of(T, ...) jf_list_of_##T((T[]){__VA_ARGS__}, (int) (sizeof((T[]){__VA_ARGS__}) / sizeof(T)))\n");
            sb.Append("#define set_of(T, ...) jf_set_of_##T((T[]){__VA_ARGS__}, (int) (sizeof((T[]){__VA_ARGS__}) / sizeof(T)))\n");
            sb.Append("#define map_of(K, V, ...) jf_map_of_##K##_##V((struct jf_pair_##K##_##V[]){__VA_ARGS__}, (int) (sizeof((struct jf_pair_##K##_##V[]){__VA_ARGS__}) / sizeof(struct jf_pair_##K##_##V)))\n");
            void ListOp(string name, string args)
            {
                sb.Append("#define list_").Append(name).Append("(l").Append(args.Length > 0 ? ", " + args : "").Append(") _Generic((l), ")
                  .Append(string.Join(", ", CElems.Select(e => "List_" + e + ": jf_list_" + name + "_" + e))).Append(")(l").Append(args.Length > 0 ? ", " + args : "").Append(")\n");
            }
            ListOp("add", "x"); ListOp("get", "i"); ListOp("set", "i, x"); ListOp("len", ""); ListOp("insert", "i, x"); ListOp("pop", "");
            ListOp("remove_at", "i"); ListOp("remove", "x"); ListOp("contains", "x"); ListOp("index_of", "x"); ListOp("clear", ""); ListOp("copy", ""); ListOp("is_empty", "");
            void SetOp(string name, string args)
            {
                sb.Append("#define set_").Append(name).Append("(s").Append(args.Length > 0 ? ", " + args : "").Append(") _Generic((s), ")
                  .Append(string.Join(", ", CElems.Select(e => "Set_" + e + ": jf_set_" + name + "_" + e))).Append(")(s").Append(args.Length > 0 ? ", " + args : "").Append(")\n");
            }
            SetOp("add", "x"); SetOp("remove", "x"); SetOp("contains", "x"); SetOp("len", ""); SetOp("clear", ""); SetOp("copy", ""); SetOp("is_empty", "");
            void MapOp(string name, string args)
            {
                sb.Append("#define map_").Append(name).Append("(m").Append(args.Length > 0 ? ", " + args : "").Append(") _Generic((m), ")
                  .Append(string.Join(", ", maps.Select(m => m.T + ": jf_map_" + name + "_" + m.K + "_" + m.V))).Append(")(m").Append(args.Length > 0 ? ", " + args : "").Append(")\n");
            }
            MapOp("put", "k, v"); MapOp("get", "k"); MapOp("get_or", "k, d"); MapOp("has", "k"); MapOp("remove", "k"); MapOp("len", "");
            MapOp("keys", ""); MapOp("values", ""); MapOp("clear", ""); MapOp("copy", ""); MapOp("is_empty", "");
            // for_each: element i of a list / set, key i of a map
            sb.Append("#define jf_size(c) _Generic((c), ").Append(Assoc(lists, t => "jf_list_len_" + t.Substring(5)))
              .Append(", ").Append(Assoc(sets, t => "jf_set_len_" + t.Substring(4)))
              .Append(", ").Append(string.Join(", ", maps.Select(m => m.T + ": jf_map_len_" + m.K + "_" + m.V))).Append(")(c)\n");
            sb.Append("#define jf_at(c, i) _Generic((c), ").Append(Assoc(lists, t => "jf_list_get_" + t.Substring(5)))
              .Append(", ").Append(Assoc(sets, t => "jf_set_at_" + t.Substring(4)))
              .Append(", ").Append(string.Join(", ", maps.Select(m => m.T + ": jf_map_key_at_" + m.K + "_" + m.V))).Append(")(c, i)\n");
            sb.Append(@"// for_each (int x, xs) { ... }: break and continue work like in any loop
#define for_each(decl, coll) \
    for (int jf_i_ = 0, jf_k_ = 1; jf_k_ && jf_i_ < jf_size(coll); jf_i_++) \
        for (decl = (jf_k_ = 0, jf_at(coll, jf_i_)); !jf_k_; jf_k_ = 1)
");
            // str_of
            sb.Append("#define str_of(x) _Generic((x), int: jf_str_int, long: jf_str_long, double: jf_str_double, float: jf_str_double, bool: jf_str_bool, char *: jf_str_string, const char *: jf_str_string, JfObj: jf_str_JfObj, Drone: jf_str_Drone, Position: jf_str_Position, Companion: jf_str_Companion, ")
              .Append(Assoc(lists, t => "jf_str_" + t)).Append(", ").Append(Assoc(sets, t => "jf_str_" + t)).Append(", ")
              .Append(string.Join(", ", maps.Select(m => m.T + ": jf_str_" + m.T))).Append(")(x)\n");
            return sb.ToString();
        }

        const string CText = @"
// ---------------------------------------------------------------- strings
static inline char *jf_strdup(const char *s) { size_t n = strlen(s) + 1; char *r = malloc(n); memcpy(r, s, n); return r; }
/// Joins two strings (the result is a new string; it is never freed).
static inline const char *str_concat(const char *a, const char *b) { size_t la = strlen(a), lb = strlen(b); char *r = malloc(la + lb + 1); memcpy(r, a, la); memcpy(r + la, b, lb + 1); return r; }
static inline bool str_eq(const char *a, const char *b) { return strcmp(a, b) == 0; }
static inline bool str_contains(const char *s, const char *part) { return strstr(s, part) != NULL; }
static inline const char *jf_str_int(int x) { char b[32]; snprintf(b, sizeof b, ""%d"", x); return jf_strdup(b); }
static inline const char *jf_str_long(long x) { char b[32]; snprintf(b, sizeof b, ""%ld"", x); return jf_strdup(b); }
/// numbers print like the game prints them: at most 2 decimals, no trailing zeros
static inline const char *jf_str_double(double x) { char b[64]; snprintf(b, sizeof b, ""%.2f"", x); char *e = b + strlen(b) - 1; while (*e == '0') *e-- = 0; if (*e == '.') *e = 0; if (strcmp(b, ""-0"") == 0) strcpy(b, ""0""); return jf_strdup(b); }
static inline const char *jf_str_bool(bool x) { return x ? ""true"" : ""false""; }
static inline const char *jf_str_string(const char *x) { return x ? x : ""NULL""; }
static inline const char *jf_str_JfObj(JfObj x) { return x ? x->name : ""NULL""; }
static inline const char *jf_str_Drone(Drone x) { if (!x) return ""NULL""; char b[32]; snprintf(b, sizeof b, ""Drone %d"", x->id); return jf_strdup(b); }
static inline const char *jf_str_Position(Position p) { char b[64]; snprintf(b, sizeof b, ""{%d, %d}"", p.x, p.y); return jf_strdup(b); }
static inline const char *jf_str_Companion(Companion c) { if (!c) return ""NULL""; return str_concat(str_concat(str_concat(""{"", jf_str_JfObj(c->entity)), str_concat("", "", jf_str_Position(c->position))), ""}""); }
static inline const char *jf_quote(const char *s) { return str_concat(str_concat(""\"""", s), ""\""""); }
#define JF_FMT_int(x) jf_str_int(x)
#define JF_FMT_double(x) jf_str_double(x)
#define JF_FMT_bool(x) jf_str_bool(x)
#define JF_FMT_string(x) jf_quote(x)
#define JF_FMT_Entity(x) jf_str_JfObj(x)
#define JF_FMT_Ground(x) jf_str_JfObj(x)
#define JF_FMT_Item(x) jf_str_JfObj(x)
#define JF_FMT_Hat(x) jf_str_JfObj(x)
#define JF_FMT_Unlock(x) jf_str_JfObj(x)
#define JF_FMT_Leaderboard(x) jf_str_JfObj(x)
#define JF_FMT_Direction(x) jf_str_JfObj(x)
#define JF_FMT_Drone(x) jf_str_Drone(x)
#define JF_FMT_Position(x) jf_str_Position(x)
#define JF_EQ_int(a, b) ((a) == (b))
#define JF_EQ_double(a, b) ((a) == (b))
#define JF_EQ_bool(a, b) ((a) == (b))
#define JF_EQ_string(a, b) (strcmp((a), (b)) == 0)
#define JF_EQ_Entity(a, b) ((a) == (b))
#define JF_EQ_Ground(a, b) ((a) == (b))
#define JF_EQ_Item(a, b) ((a) == (b))
#define JF_EQ_Hat(a, b) ((a) == (b))
#define JF_EQ_Unlock(a, b) ((a) == (b))
#define JF_EQ_Leaderboard(a, b) ((a) == (b))
#define JF_EQ_Direction(a, b) ((a) == (b))
#define JF_EQ_Drone(a, b) ((a) == (b))
#define JF_EQ_Position(a, b) ((a).x == (b).x && (a).y == (b).y)

// ---------------------------------------------------------------- printing
/// quick_print: like printf, but prints one line to the output page only (a trailing \n is optional).
[[gnu::format(printf, 1, 2)]] static inline void quick_print(const char *format, ...)
{
    va_list a; va_start(a, format); vprintf(format, a); va_end(a);
    size_t n = strlen(format);
    if (n == 0 || format[n - 1] != '\n') putchar('\n');
}

// ---------------------------------------------------------------- collections (one type per element type)
#define JF_LIST(T) \
typedef struct jf_list_##T { T *items; int len, cap; } *List_##T; \
static inline List_##T jf_list_new_##T(void) { return calloc(1, sizeof(struct jf_list_##T)); } \
static inline void jf_list_add_##T(List_##T l, T x) { if (l->len == l->cap) { l->cap = l->cap ? l->cap * 2 : 8; l->items = realloc(l->items, (size_t) l->cap * sizeof(T)); } l->items[l->len++] = x; } \
static inline List_##T jf_list_of_##T(const T *xs, int n) { List_##T l = jf_list_new_##T(); for (int i = 0; i < n; i++) jf_list_add_##T(l, xs[i]); return l; } \
static inline int jf_list_len_##T(List_##T l) { return l->len; } \
static inline bool jf_list_is_empty_##T(List_##T l) { return l->len == 0; } \
static inline T jf_list_get_##T(List_##T l, int i) { if (i < 0) i += l->len; if (i < 0 || i >= l->len) jf_fail(""list index out of range""); return l->items[i]; } \
static inline void jf_list_set_##T(List_##T l, int i, T x) { if (i < 0) i += l->len; if (i < 0 || i >= l->len) jf_fail(""list index out of range""); l->items[i] = x; } \
static inline void jf_list_insert_##T(List_##T l, int i, T x) { if (i < 0) i += l->len; if (i < 0) i = 0; if (i > l->len) i = l->len; jf_list_add_##T(l, x); memmove(l->items + i + 1, l->items + i, (size_t) (l->len - 1 - i) * sizeof(T)); l->items[i] = x; } \
static inline T jf_list_remove_at_##T(List_##T l, int i) { if (i < 0) i += l->len; if (i < 0 || i >= l->len) jf_fail(""pop index out of range""); T x = l->items[i]; memmove(l->items + i, l->items + i + 1, (size_t) (l->len - 1 - i) * sizeof(T)); l->len--; return x; } \
static inline T jf_list_pop_##T(List_##T l) { if (l->len == 0) jf_fail(""pop from an empty list""); return l->items[--l->len]; } \
static inline int jf_list_index_of_##T(List_##T l, T x) { for (int i = 0; i < l->len; i++) if (JF_EQ_##T(l->items[i], x)) return i; return -1; } \
static inline bool jf_list_contains_##T(List_##T l, T x) { return jf_list_index_of_##T(l, x) >= 0; } \
static inline void jf_list_remove_##T(List_##T l, T x) { int i = jf_list_index_of_##T(l, x); if (i < 0) jf_fail(""list_remove: the value is not in the list""); jf_list_remove_at_##T(l, i); } \
static inline void jf_list_clear_##T(List_##T l) { l->len = 0; } \
static inline List_##T jf_list_copy_##T(List_##T l) { return jf_list_of_##T(l->items, l->len); } \
static inline const char *jf_str_List_##T(List_##T l) { const char *s = ""{""; for (int i = 0; i < l->len; i++) s = str_concat(str_concat(s, i ? "", "" : """"), JF_FMT_##T(l->items[i])); return str_concat(s, ""}""); }

#define JF_SET(T) \
typedef struct jf_set_##T { T *items; int len, cap; } *Set_##T; \
static inline Set_##T jf_set_new_##T(void) { return calloc(1, sizeof(struct jf_set_##T)); } \
static inline int jf_set_find_##T(Set_##T s, T x) { for (int i = 0; i < s->len; i++) if (JF_EQ_##T(s->items[i], x)) return i; return -1; } \
static inline void jf_set_add_##T(Set_##T s, T x) { if (jf_set_find_##T(s, x) >= 0) return; if (s->len == s->cap) { s->cap = s->cap ? s->cap * 2 : 8; s->items = realloc(s->items, (size_t) s->cap * sizeof(T)); } s->items[s->len++] = x; } \
static inline Set_##T jf_set_of_##T(const T *xs, int n) { Set_##T s = jf_set_new_##T(); for (int i = 0; i < n; i++) jf_set_add_##T(s, xs[i]); return s; } \
static inline int jf_set_len_##T(Set_##T s) { return s->len; } \
static inline bool jf_set_is_empty_##T(Set_##T s) { return s->len == 0; } \
static inline T jf_set_at_##T(Set_##T s, int i) { return s->items[i]; } \
static inline bool jf_set_contains_##T(Set_##T s, T x) { return jf_set_find_##T(s, x) >= 0; } \
static inline void jf_set_remove_##T(Set_##T s, T x) { int i = jf_set_find_##T(s, x); if (i < 0) jf_fail(""set_remove: the value is not in the set""); memmove(s->items + i, s->items + i + 1, (size_t) (s->len - 1 - i) * sizeof(T)); s->len--; } \
static inline void jf_set_clear_##T(Set_##T s) { s->len = 0; } \
static inline Set_##T jf_set_copy_##T(Set_##T s) { return jf_set_of_##T(s->items, s->len); } \
static inline const char *jf_str_Set_##T(Set_##T l) { const char *s = ""{""; for (int i = 0; i < l->len; i++) s = str_concat(str_concat(s, i ? "", "" : """"), JF_FMT_##T(l->items[i])); return str_concat(s, ""}""); }

#define JF_MAP(K, V) \
struct jf_pair_##K##_##V { K key; V value; }; \
typedef struct jf_map_##K##_##V { K *keys; V *values; int len, cap; } *Map_##K##_##V; \
static inline Map_##K##_##V jf_map_new_##K##_##V(void) { return calloc(1, sizeof(struct jf_map_##K##_##V)); } \
static inline int jf_map_find_##K##_##V(Map_##K##_##V m, K k) { for (int i = 0; i < m->len; i++) if (JF_EQ_##K(m->keys[i], k)) return i; return -1; } \
static inline void jf_map_put_##K##_##V(Map_##K##_##V m, K k, V v) { int i = jf_map_find_##K##_##V(m, k); if (i >= 0) { m->values[i] = v; return; } if (m->len == m->cap) { m->cap = m->cap ? m->cap * 2 : 8; m->keys = realloc(m->keys, (size_t) m->cap * sizeof(K)); m->values = realloc(m->values, (size_t) m->cap * sizeof(V)); } m->keys[m->len] = k; m->values[m->len++] = v; } \
static inline Map_##K##_##V jf_map_of_##K##_##V(const struct jf_pair_##K##_##V *ps, int n) { Map_##K##_##V m = jf_map_new_##K##_##V(); for (int i = 0; i < n; i++) jf_map_put_##K##_##V(m, ps[i].key, ps[i].value); return m; } \
static inline V jf_map_get_##K##_##V(Map_##K##_##V m, K k) { int i = jf_map_find_##K##_##V(m, k); if (i < 0) jf_fail(""map_get: the key is not in the map""); return m->values[i]; } \
static inline V jf_map_get_or_##K##_##V(Map_##K##_##V m, K k, V d) { int i = jf_map_find_##K##_##V(m, k); return i < 0 ? d : m->values[i]; } \
static inline bool jf_map_has_##K##_##V(Map_##K##_##V m, K k) { return jf_map_find_##K##_##V(m, k) >= 0; } \
static inline V jf_map_remove_##K##_##V(Map_##K##_##V m, K k) { int i = jf_map_find_##K##_##V(m, k); if (i < 0) jf_fail(""map_remove: the key is not in the map""); V v = m->values[i]; memmove(m->keys + i, m->keys + i + 1, (size_t) (m->len - 1 - i) * sizeof(K)); memmove(m->values + i, m->values + i + 1, (size_t) (m->len - 1 - i) * sizeof(V)); m->len--; return v; } \
static inline int jf_map_len_##K##_##V(Map_##K##_##V m) { return m->len; } \
static inline bool jf_map_is_empty_##K##_##V(Map_##K##_##V m) { return m->len == 0; } \
static inline K jf_map_key_at_##K##_##V(Map_##K##_##V m, int i) { return m->keys[i]; } \
static inline List_##K jf_map_keys_##K##_##V(Map_##K##_##V m) { return jf_list_of_##K(m->keys, m->len); } \
static inline List_##V jf_map_values_##K##_##V(Map_##K##_##V m) { return jf_list_of_##V(m->values, m->len); } \
static inline void jf_map_clear_##K##_##V(Map_##K##_##V m) { m->len = 0; } \
static inline Map_##K##_##V jf_map_copy_##K##_##V(Map_##K##_##V m) { Map_##K##_##V c = jf_map_new_##K##_##V(); for (int i = 0; i < m->len; i++) jf_map_put_##K##_##V(c, m->keys[i], m->values[i]); return c; } \
static inline const char *jf_str_Map_##K##_##V(Map_##K##_##V m) { const char *s = ""{""; for (int i = 0; i < m->len; i++) s = str_concat(str_concat(str_concat(s, i ? "", "" : """"), JF_FMT_##K(m->keys[i])), str_concat("": "", JF_FMT_##V(m->values[i]))); return str_concat(s, ""}""); }

";

        static string CApi()
        {
            var sb = new StringBuilder(@"
// ---------------------------------------------------------------- game functions (they do nothing outside the game)
#define JF_PICK(_0, _1, _2, _3, _4, _5, _6, _7, _8, N, ...) N
#undef min
#undef max
#define min(a, b) _Generic((a) + (b), int: jf_min_int, long: jf_min_long, default: jf_min_double)(a, b)
#define max(a, b) _Generic((a) + (b), int: jf_max_int, long: jf_max_long, default: jf_max_double)(a, b)
static inline int jf_min_int(int a, int b) { return a < b ? a : b; }
static inline long jf_min_long(long a, long b) { return a < b ? a : b; }
static inline double jf_min_double(double a, double b) { return a < b ? a : b; }
static inline int jf_max_int(int a, int b) { return a > b ? a : b; }
static inline long jf_max_long(long a, long b) { return a > b ? a : b; }
static inline double jf_max_double(double a, double b) { return a > b ? a : b; }
#define abs(x) _Generic((x), int: jf_abs_int, long: jf_abs_long, default: jf_abs_double)(x)
static inline int jf_abs_int(int x) { return x < 0 ? -x : x; }
static inline long jf_abs_long(long x) { return x < 0 ? -x : x; }
static inline double jf_abs_double(double x) { return x < 0 ? -x : x; }
/// A random number in [0, 1).
static inline double random(void) { return rand() / (RAND_MAX + 1.0); }
/// measure() when the result is a position (the next treasure in a maze, the dinosaur's apple): measure_pos() or measure_pos(dir).
#define measure_pos(...) jf_measure_pos(0 __VA_OPT__(,) __VA_ARGS__)
static inline Position jf_measure_pos(int unused, ...) { (void) unused; return (Position){0, 0}; }
");
            foreach (var b in GameApi.Builtins)
            {
                switch (b.PythonName)
                {
                    case "print": case "quick_print": case "min": case "max": case "abs": case "random": continue;
                }
                sb.Append("/// ").Append(b.Doc).Append(" (game: ").Append(b.Signature).Append(")\n");
                var ret = CRet(b);
                var reqs = b.Params.Where(p => !p.Optional && !p.Variadic).ToList();
                var opts = b.Params.Where(p => p.Optional && !p.Variadic).ToList();
                bool variadic = b.Params.Any(p => p.Variadic) || b.Params.Any(p => p.TypeText == "any" && b.PythonName != "simulate");
                if (variadic)
                {
                    var named = reqs.Where(p => p.TypeText != "any").ToList();
                    sb.Append("static inline ").Append(ret).Append(ret.EndsWith("*") ? "" : " ").Append(b.PythonName).Append('(')
                      .Append(string.Join(", ", named.Select(p => CParam(b, p)).Concat(new[] { "..." }))).Append(") { ").Append(Body(ret, named)).Append("}\n");
                    continue;
                }
                if (opts.Count == 0)
                {
                    sb.Append("static inline ").Append(ret).Append(ret.EndsWith("*") ? "" : " ").Append(b.PythonName).Append('(')
                      .Append(reqs.Count == 0 ? "void" : string.Join(", ", reqs.Select(p => CParam(b, p)))).Append(") { ").Append(Body(ret, reqs)).Append("}\n");
                    continue;
                }
                // optional parameters: one function per argument count, picked by a macro
                var all = reqs.Concat(opts).ToList();
                for (int n = reqs.Count; n <= all.Count; n++)
                {
                    var ps = all.Take(n).ToList();
                    sb.Append("static inline ").Append(ret).Append(ret.EndsWith("*") ? "" : " ").Append("jf_").Append(b.PythonName).Append('_').Append(n).Append('(')
                      .Append(ps.Count == 0 ? "void" : string.Join(", ", ps.Select(p => CParam(b, p)))).Append(") { ").Append(Body(ret, ps)).Append("}\n");
                }
                var picks = new List<string>();
                for (int n = 8; n >= 0; n--) picks.Add(n >= reqs.Count && n <= all.Count ? "jf_" + b.PythonName + "_" + n : "jf_wrong_number_of_arguments_to_" + b.PythonName);
                sb.Append("#define ").Append(b.PythonName).Append("(...) JF_PICK(_ __VA_OPT__(,) __VA_ARGS__, ").Append(string.Join(", ", picks)).Append(")(__VA_ARGS__)\n");
            }
            return sb.ToString();
        }

        static string CRet(ApiBuiltin b)
        {
            switch (b.PythonName)
            {
                case "measure": return "double";
                case "wait_for": return "void";
                case "get_cost": return "Map(Item, double)";
                case "get_companion": return "Companion";
                case "spawn_drone": return "Drone";
            }
            return CTypeOf(b.ReturnType, b.ReturnText);
        }

        static string CParam(ApiBuiltin b, ApiParam p)
        {
            if (p.TypeText == "func") return "void (*" + p.Name + ")(void)";
            if (b.PythonName == "simulate" && p.Name == "sim_unlocks") return "Map(Unlock, int) " + p.Name;
            if (b.PythonName == "simulate" && p.Name == "sim_globals") return "Map(string, int) " + p.Name;
            var t = p.TypeText == "number" ? "double" : CTypeOf(p.Type, p.TypeText);
            return t + (t.EndsWith("*") ? "" : " ") + p.Name;
        }

        static string Body(string ret, List<ApiParam> ps)
        {
            var sb = new StringBuilder();
            foreach (var p in ps) sb.Append("(void) ").Append(p.Name).Append("; ");
            var z = Zero(ret);
            if (z != null) sb.Append("return ").Append(z).Append("; ");
            return sb.ToString();
        }

        // ================================================================== C++

        public static string CppHeader(IEnumerable<string> sources)
        {
            var members = Members(sources, "::");
            var sb = new StringBuilder();
            sb.Append(@"// farm.hpp - the game API of The Farmer Was Replaced for C++ (C++23), written by the JavaFarmer mod.
// #include ""farm.hpp"" in every code window. The game never compiles this file: it documents the API for
// external editors and makes JavaFarmer programs real C++. Outside the game the farm functions do nothing,
//     g++ -std=c++23 -Wall main.cpp
// builds and runs a program that only computes and prints.
// Game values are small handle types (== nullptr when there is none). Constants keep the game's names:
// Entities::Carrot, Items::Hay, North (also Direction::North). get_cost / get_companion return std::optional.
#pragma once
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <format>
#include <functional>
#include <map>
#include <optional>
#include <ostream>
#include <set>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace farm {
    struct DirectionTag;
    template <class Tag> struct Handle;
    template <class Tag> struct HandleStatics { };
    template <> struct HandleStatics<DirectionTag> { static const Handle<DirectionTag> North, East, South, West; };

    /// A game value (Entities::Carrot, North ...); a default-constructed handle is nullptr.
    template <class Tag> struct Handle : HandleStatics<Tag> {
        const char* name = nullptr;
        constexpr Handle() = default;
        constexpr Handle(std::nullptr_t) { }
        constexpr explicit Handle(const char* n) : name(n) { }
        constexpr bool operator==(const Handle& o) const { return std::string_view(name ? name : """") == std::string_view(o.name ? o.name : """"); }
        constexpr bool operator==(std::nullptr_t) const { return name == nullptr; }
        constexpr auto operator<=>(const Handle& o) const { return std::string_view(name ? name : """") <=> std::string_view(o.name ? o.name : """"); }
        constexpr explicit operator bool() const { return name != nullptr; }
    };
");
            foreach (var h in Handles)
            {
                if (h != "Direction") sb.Append("    struct ").Append(h).Append("Tag;\n");
                sb.Append("    using ").Append(h).Append(" = Handle<").Append(h).Append("Tag>;\n");
            }
            sb.Append(@"    inline const Direction HandleStatics<DirectionTag>::North{""North""}, HandleStatics<DirectionTag>::East{""East""}, HandleStatics<DirectionTag>::South{""South""}, HandleStatics<DirectionTag>::West{""West""};
    inline const Direction North = Direction::North, East = Direction::East, South = Direction::South, West = Direction::West;

    /// A spawned drone (nullptr when no drone was available).
    struct Drone {
        int id = 0;
        constexpr Drone() = default;
        constexpr Drone(std::nullptr_t) { }
        constexpr bool operator==(const Drone& o) const = default;
        constexpr bool operator==(std::nullptr_t) const { return id == 0; }
        constexpr explicit operator bool() const { return id != 0; }
    };
    struct Position {
        int x = 0, y = 0;
        constexpr bool operator==(const Position&) const = default;
        constexpr auto operator<=>(const Position&) const = default;
    };
    struct Companion { Entity entity; Position position; };
    /// The result of measure(): a number, or a Position (treasure / apple).
    struct Value {
        double number = 0; Position position;
        operator double() const { return number; }
        operator int() const { return (int) number; }
        operator Position() const { return position; }
    };
");
            foreach (var bag in GameApi.ConstantCategories)
            {
                var type = GameApi.GameTypeOfCategory(bag);
                sb.Append("    namespace ").Append(bag).Append(" {");
                foreach (var m in members[bag]) sb.Append(" inline const ").Append(type).Append(' ').Append(m).Append("{\"").Append(bag).Append("::").Append(m).Append("\"};");
                sb.Append(" }\n");
            }
            sb.Append(@"
    /// quick_print(""{} {}"", a, b): prints one line to the output page only.
    template <class... A> void quick_print(std::format_string<A...> format, A&&... args) { std::puts(std::format(format, std::forward<A>(args)...).c_str()); }
    /// A random number in [0, 1). Write farm::random(): the C library has a random() too.
    inline double random() { return std::rand() / (RAND_MAX + 1.0); }
    /// Starts a drone running the function (or lambda); nullptr when no drone is available.
    template <class F> Drone spawn_drone(F&& task) { (void) task; return nullptr; }
");
            foreach (var b in GameApi.Builtins)
            {
                switch (b.PythonName)
                {
                    case "print": case "quick_print": case "min": case "max": case "abs": case "random": case "spawn_drone": continue;
                }
                sb.Append("    /// ").Append(b.Doc).Append(" (game: ").Append(b.Signature).Append(")\n");
                var ret = CppRet(b);
                // union parameters: one overload per alternative
                var variants = new List<List<string>> { new List<string>() };
                bool variadic = false;
                foreach (var p in b.Params)
                {
                    if (p.Variadic || (p.TypeText == "any" && b.PythonName != "simulate")) { variadic = true; break; }
                    var alts = p.TypeText.Contains("|") ? p.TypeText.Split('|').Select(x => x.Trim()).ToList() : new List<string> { CppType(b, p) };
                    var dflt = p.Optional ? " = " + CppDefault(alts[0]) : "";
                    variants = variants.SelectMany(v => alts.Select(a => v.Concat(new[] { a + " " + p.Name + dflt }).ToList())).ToList();
                }
                foreach (var v in variants)
                {
                    var ps = v.ToList();
                    if (variadic) ps.Add("auto&&...");
                    var z = CppZero(ret);
                    sb.Append("    inline ").Append(ret).Append(' ').Append(b.PythonName).Append('(').Append(string.Join(", ", ps)).Append(") { ")
                      .Append(z == null ? "" : "return " + z + "; ").Append("}\n");
                }
            }
            sb.Append(@"}

using namespace farm;

template <class Tag> struct std::hash<farm::Handle<Tag>> { size_t operator()(const farm::Handle<Tag>& h) const { return std::hash<std::string_view>()(h.name ? h.name : """"); } };
template <> struct std::hash<farm::Position> { size_t operator()(const farm::Position& p) const { return std::hash<long long>()(((long long) p.x << 32) ^ (unsigned) p.y); } };
template <> struct std::hash<farm::Drone> { size_t operator()(const farm::Drone& d) const { return std::hash<int>()(d.id); } };
template <class Tag> struct std::formatter<farm::Handle<Tag>> : std::formatter<std::string_view> {
    auto format(const farm::Handle<Tag>& h, std::format_context& ctx) const { return std::formatter<std::string_view>::format(h.name ? h.name : ""nullptr"", ctx); }
};
template <> struct std::formatter<farm::Position> : std::formatter<std::string> {
    auto format(const farm::Position& p, std::format_context& ctx) const { return std::formatter<std::string>::format(std::format(""({}, {})"", p.x, p.y), ctx); }
};
template <> struct std::formatter<farm::Drone> : std::formatter<std::string> {
    auto format(const farm::Drone& d, std::format_context& ctx) const { return std::formatter<std::string>::format(d.id ? std::format(""Drone {}"", d.id) : ""nullptr"", ctx); }
};
template <> struct std::formatter<farm::Value> : std::formatter<double> {
    auto format(const farm::Value& v, std::format_context& ctx) const { return std::formatter<double>::format(v.number, ctx); }
};
template <class Tag> std::ostream& operator<<(std::ostream& o, const farm::Handle<Tag>& h) { return o << (h.name ? h.name : ""nullptr""); }
inline std::ostream& operator<<(std::ostream& o, const farm::Position& p) { return o << '(' << p.x << "", "" << p.y << ')'; }
inline std::ostream& operator<<(std::ostream& o, const farm::Drone& d) { return d.id ? o << ""Drone "" << d.id : o << ""nullptr""; }
inline std::ostream& operator<<(std::ostream& o, const farm::Value& v) { return o << v.number; }
");
            return sb.ToString();
        }

        static string CppRet(ApiBuiltin b)
        {
            switch (b.PythonName)
            {
                case "measure": return "Value";
                case "wait_for": return "void";
                case "get_cost": return "std::optional<std::map<Item, double>>";
                case "get_companion": return "std::optional<Companion>";
                case "simulate": return "double";
            }
            return CppTypeOf(b.ReturnType);
        }

        static string CppType(ApiBuiltin b, ApiParam p)
        {
            if (b.PythonName == "simulate" && p.Name == "sim_unlocks") return "const std::map<Unlock, int>&";
            if (b.PythonName == "simulate" && p.Name == "sim_globals") return "const std::map<std::string, int>&";
            if (p.TypeText == "number") return "double";
            if (p.TypeText == "func") return "std::function<void()>";
            var t = CppTypeOf(p.Type);
            return t.StartsWith("std::") ? "const " + t + "&" : t;
        }

        static string CppTypeOf(IrType t)
        {
            if (t == null) return "double";
            string r;
            switch (t.Kind)
            {
                case IrTypeKind.Void: return "void";
                case IrTypeKind.Int: return "int";
                case IrTypeKind.Float: return "double";
                case IrTypeKind.Bool: return "bool";
                case IrTypeKind.String: return "std::string";
                case IrTypeKind.Game: return t.GameName;
                case IrTypeKind.List: case IrTypeKind.Array: r = "std::vector<" + CppTypeOf(t.Elem) + ">"; break;
                case IrTypeKind.Set: r = "std::set<" + CppTypeOf(t.Elem) + ">"; break;
                case IrTypeKind.Map: r = "std::map<" + CppTypeOf(t.Key) + ", " + CppTypeOf(t.Elem) + ">"; break;
                default: return "Value";
            }
            return t.Nullable ? "std::optional<" + r + ">" : r;
        }

        static string CppDefault(string type)
        {
            switch (type)
            {
                case "int": case "double": return "0";
                case "bool": return "false";
            }
            return "{}";
        }

        static string CppZero(string ret)
        {
            switch (ret)
            {
                case "void": return null;
                case "int": case "double": return "0";
                case "bool": return "false";
            }
            return "{}";
        }
    }
}
