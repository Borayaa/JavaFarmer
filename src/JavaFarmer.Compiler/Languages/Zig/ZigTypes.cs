using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.ZigImpl
{
    internal enum ZK
    {
        Error,       // already reported; suppresses follow-up errors
        Void, NoReturn, Bool,
        Int,         // runtime integer type (Bits, Signed, Name: i32 / usize / u8 ...)
        CInt,        // comptime_int
        Float,       // f32 / f64
        CFloat,      // comptime_float
        Null, Undefined,
        EnumLit,     // .North without a known type
        StrLit,      // *const [N:0]u8
        Slice,       // []T / []const T
        Array,       // [N]T
        Optional,    // ?T
        ErrorUnion,  // !T
        Game,        // farm enum: Entity, Item, Ground, Unlock, Hat, Leaderboard, Direction
        Struct,      // farm.Position, farm.Companion, farm.Drone
        List,        // std.ArrayList(T)
        Map,         // std.AutoHashMap(K, V) (V void = set)
        Iter,        // map.iterator() / keyIterator() / valueIterator()
        Entry,       // iterator entry (key_ptr, value_ptr)
        Ptr,         // *T (entry.key_ptr, &x)
        Tuple,       // .{ a, b } / struct { T, U }
        Fn,          // a function (by name)
        FnType,      // fn () void as a type
        Type,        // a type used as a value (i32, farm.Entity)
        Namespace,   // std, std.debug, farm, another file ...
        Allocator,   // farm.allocator / std.mem.Allocator
        Foreign,     // a value from a file in a dynamic language (Python, JS, R): no Zig type, coerces to any type
    }

    internal sealed class ZFnSig
    {
        public List<ZType> Params = new List<ZType>();
        public List<string> ParamNames = new List<string>();
        public ZType Ret;
        public bool RetError;            // !T
        public ZFnDecl Decl;
        /// <summary>Fewest arguments a call may pass (functions of other languages with default values); -1 = all.</summary>
        public int MinParams = -1;
        public bool IsPub;
        public string Module;            // other file, null = this file
    }

    internal sealed class ZType
    {
        public ZK K;
        public int Bits;
        public bool Signed;
        public string Name;          // Int: "i32"/"usize"; Game: "Entity"; Struct: "Position"; Namespace: path; EnumLit: member
        public ZType Elem, Key;      // Slice/Array/Optional/ErrorUnion/List/Ptr elem; Map value (Elem) and Key
        public long Len;             // Array / StrLit length
        public bool Const;           // Slice / Ptr const
        public List<ZType> Items;    // Tuple
        public ZFnSig Sig;           // Fn / FnType
        public ZType Inner;          // Type: the denoted type
        public string IterKind;      // Iter: "entries" / "keys" / "values"
        public ZSym Sym;             // Fn: the function symbol

        public static readonly ZType Err = new ZType { K = ZK.Error };
        public static readonly ZType Void = new ZType { K = ZK.Void };
        public static readonly ZType NoReturn = new ZType { K = ZK.NoReturn };
        public static readonly ZType Bool = new ZType { K = ZK.Bool };
        public static readonly ZType CInt = new ZType { K = ZK.CInt };
        public static readonly ZType CFloat = new ZType { K = ZK.CFloat };
        public static readonly ZType Null = new ZType { K = ZK.Null };
        public static readonly ZType Undefined = new ZType { K = ZK.Undefined };
        public static readonly ZType F64 = new ZType { K = ZK.Float, Bits = 64, Name = "f64" };
        public static readonly ZType F32 = new ZType { K = ZK.Float, Bits = 32, Name = "f32" };
        public static readonly ZType I32 = IntT("i32");
        public static readonly ZType Usize = IntT("usize");
        public static readonly ZType U32 = IntT("u32");
        public static readonly ZType U8 = IntT("u8");
        public static readonly ZType Str = new ZType { K = ZK.Slice, Elem = U8, Const = true };
        public static readonly ZType Alloc = new ZType { K = ZK.Allocator };
        public static readonly ZType TypeOfType = new ZType { K = ZK.Type };
        public static readonly ZType Foreign = new ZType { K = ZK.Foreign };

        /// <summary>i32 / u8 / usize / isize; null for other names.</summary>
        public static ZType IntT(string name)
        {
            if (name == "usize") return new ZType { K = ZK.Int, Bits = 64, Signed = false, Name = name };
            if (name == "isize") return new ZType { K = ZK.Int, Bits = 64, Signed = true, Name = name };
            if (name.Length < 2 || (name[0] != 'i' && name[0] != 'u')) return null;
            for (int i = 1; i < name.Length; i++) if (!char.IsDigit(name[i])) return null;
            if (name.Length > 1 && name[1] == '0' && name.Length > 2) return null;
            if (!int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int bits) || bits > 65535) return null;
            return new ZType { K = ZK.Int, Bits = bits, Signed = name[0] == 'i', Name = name };
        }

        public static ZType Opt(ZType t) => t.K == ZK.Optional || t.K == ZK.Error ? t : new ZType { K = ZK.Optional, Elem = t };
        public static ZType SliceOf(ZType e, bool cst) => new ZType { K = ZK.Slice, Elem = e, Const = cst };
        public static ZType ArrayOf(ZType e, long n) => new ZType { K = ZK.Array, Elem = e, Len = n };
        public static ZType ListOf(ZType e) => new ZType { K = ZK.List, Elem = e };
        public static ZType MapOf(ZType k, ZType v) => new ZType { K = ZK.Map, Key = k, Elem = v };
        public static ZType GameT(string n) => new ZType { K = ZK.Game, Name = n };
        public static ZType StructT(string n) => new ZType { K = ZK.Struct, Name = n };
        public static ZType Ns(string path) => new ZType { K = ZK.Namespace, Name = path };
        public static ZType TypeVal(ZType t) => new ZType { K = ZK.Type, Inner = t };
        public static ZType StrLitT(long n) => new ZType { K = ZK.StrLit, Len = n };
        public static ZType TupleT(List<ZType> items) => new ZType { K = ZK.Tuple, Items = items };
        public static ZType ErrU(ZType t) => new ZType { K = ZK.ErrorUnion, Elem = t };

        public bool IsErr => K == ZK.Error;
        public bool IsIntLike => K == ZK.Int || K == ZK.CInt;
        public bool IsFloatLike => K == ZK.Float || K == ZK.CFloat;
        public bool IsNumeric => IsIntLike || IsFloatLike;
        public bool IsComptimeNum => K == ZK.CInt || K == ZK.CFloat;
        public bool IsString => K == ZK.StrLit || (K == ZK.Slice && Elem != null && Elem.K == ZK.Int && Elem.Name == "u8");
        public bool IsSet => K == ZK.Map && Elem != null && Elem.K == ZK.Void;
        public bool IsEnum => K == ZK.Game;

        public bool Same(ZType o)
        {
            if (o == null) return false;
            if (ReferenceEquals(this, o)) return true;
            if (K != o.K) return false;
            switch (K)
            {
                case ZK.Int: return Name == o.Name;
                case ZK.Float: return Bits == o.Bits;
                case ZK.Game: case ZK.Struct: case ZK.Namespace: return Name == o.Name;
                case ZK.EnumLit: return Name == o.Name;
                case ZK.StrLit: return Len == o.Len;
                case ZK.Slice: return Const == o.Const && Elem.Same(o.Elem);
                case ZK.Ptr: return Const == o.Const && Elem.Same(o.Elem);
                case ZK.Array: return Len == o.Len && Elem.Same(o.Elem);
                case ZK.Optional: case ZK.ErrorUnion: case ZK.List: return Elem.Same(o.Elem);
                case ZK.Map: return Key.Same(o.Key) && Elem.Same(o.Elem);
                case ZK.Iter: return IterKind == o.IterKind && Key.Same(o.Key) && Elem.Same(o.Elem);
                case ZK.Entry: return Key.Same(o.Key) && Elem.Same(o.Elem);
                case ZK.Tuple: return Items.Count == o.Items.Count && Items.Zip(o.Items, (a, b) => a.Same(b)).All(x => x);
                case ZK.Type: return Inner.Same(o.Inner);
                case ZK.Fn: return Sym == o.Sym;
                case ZK.FnType: return SigSame(Sig, o.Sig);
            }
            return true;
        }

        static bool SigSame(ZFnSig a, ZFnSig b)
        {
            if (a == null || b == null) return a == b;
            if (a.Params.Count != b.Params.Count || a.RetError != b.RetError || !a.Ret.Same(b.Ret)) return false;
            for (int i = 0; i < a.Params.Count; i++) if (!a.Params[i].Same(b.Params[i])) return false;
            return true;
        }

        /// <summary>The type as Zig prints it in error messages.</summary>
        public override string ToString()
        {
            switch (K)
            {
                case ZK.Error: return "<error>";
                case ZK.Void: return "void";
                case ZK.NoReturn: return "noreturn";
                case ZK.Bool: return "bool";
                case ZK.Int: case ZK.Float: return Name;
                case ZK.CInt: return "comptime_int";
                case ZK.CFloat: return "comptime_float";
                case ZK.Null: return "@TypeOf(null)";
                case ZK.Undefined: return "@TypeOf(undefined)";
                case ZK.EnumLit: return "@EnumLiteral()";
                case ZK.StrLit: return "*const [" + Len.ToString(CultureInfo.InvariantCulture) + ":0]u8";
                case ZK.Slice: return "[]" + (Const ? "const " : "") + Elem;
                case ZK.Ptr: return "*" + (Const ? "const " : "") + Elem;
                case ZK.Array: return "[" + Len.ToString(CultureInfo.InvariantCulture) + "]" + Elem;
                case ZK.Optional: return "?" + Elem;
                case ZK.ErrorUnion: return "error{OutOfMemory}!" + Elem;
                case ZK.Game: case ZK.Struct: return "farm." + Name;
                case ZK.List: return "array_list.Aligned(" + Elem + ",null)";
                case ZK.Map: return "hash_map.HashMap(" + Key + "," + Elem + ",hash_map.AutoContext(" + Key + "),80)";
                case ZK.Iter: return "hash_map.HashMapUnmanaged(" + Key + "," + Elem + ",hash_map.AutoContext(" + Key + "),80)." + (IterKind == "entries" ? "Iterator" : IterKind == "keys" ? "FieldIterator(" + Key + ")" : "FieldIterator(" + Elem + ")");
                case ZK.Entry: return "hash_map.HashMapUnmanaged(" + Key + "," + Elem + ",hash_map.AutoContext(" + Key + "),80).Entry";
                case ZK.Tuple: return "struct { " + string.Join(", ", Items) + " }";
                case ZK.Fn: case ZK.FnType: return FnText(Sig);
                case ZK.Type: return "type";
                case ZK.Namespace: return "type";
                case ZK.Allocator: return "mem.Allocator";
                case ZK.Foreign: return "anytype";
            }
            return K.ToString();
        }

        public static string FnText(ZFnSig s)
        {
            if (s == null) return "fn () void";
            return "fn (" + string.Join(", ", s.Params) + ") " + (s.RetError ? "@typeInfo(@typeInfo(@TypeOf(f)).@\"fn\".return_type.?).error_union.error_set!" : "") + s.Ret;
        }

        /// <summary>The neutral IR type of a value of this type.</summary>
        public IrType Ir()
        {
            switch (K)
            {
                case ZK.Void: case ZK.NoReturn: return IrType.Void;
                case ZK.Bool: return IrType.Bool;
                case ZK.Int: case ZK.CInt: return IrType.Int;
                case ZK.Float: case ZK.CFloat: return IrType.Float;
                case ZK.Null: return IrType.Null;
                case ZK.StrLit: return IrType.String;
                case ZK.Slice: return IsString ? IrType.String : IrType.Array(Elem.Ir());
                case ZK.Array: return IrType.Array(Elem.Ir());
                case ZK.Optional: return Elem.Ir().AsNullable();
                case ZK.ErrorUnion: return Elem.Ir();
                case ZK.Game: return IrType.Game(Name);
                case ZK.Struct: return IrType.Game(Name);
                case ZK.List: return IrType.List(Elem.Ir());
                case ZK.Map: return IsSet ? IrType.Set(Key.Ir()) : IrType.Map(Key.Ir(), Elem.Ir());
                case ZK.Tuple: return IrType.Tuple(Items.Select(i => i.Ir()).ToArray());
                case ZK.Fn: case ZK.FnType: return IrType.Func(Sig?.Ret?.Ir() ?? IrType.Void);
                case ZK.Ptr: return Elem.Ir();
            }
            return IrType.Unknown;
        }
    }

    /// <summary>A name the checker resolves.</summary>
    internal enum ZSymKind { Local, Param, Capture, Global, Fn, Alias }

    internal sealed class ZSym
    {
        public string Name;
        public ZSymKind Kind;
        public ZType Type;
        public bool IsVar;
        public IrSymbol Ir;
        public int DeclStart, DeclEnd;
        public bool Used, Discarded, Mutated;
        public int DiscardStart, DiscardEnd;
        public string IdCat;                 // "local constant" / "local variable" / "function parameter" / "capture"
        public bool HasValue; public long IntValue; public double FloatValue; public string StrValue;   // comptime-known consts
        public ZFnSig Sig;                   // Fn
        public bool IsPub;
        public string Module;                // declared in another file
        public object Decl;                  // ZVarDecl / ZFnDecl
        public bool Resolving, Resolved;
        /// <summary>Alias: an IR expression that replaces each use (if-capture aliasing an existing variable).</summary>
        public Func<IrExpr> AliasOf;
        public override string ToString() => Name + ":" + Kind + ":" + Type;
    }
}
