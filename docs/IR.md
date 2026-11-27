# Farm IR (src/JavaFarmer.Compiler/Core/Ir.cs)

The Farm IR is the language-neutral program every frontend produces and every printer consumes:

    source ──frontend──▶ IrModule ──IrEmitter──▶ game Python + SourceMap      (Compile)
    IrModule ──printer──▶ source of any language                               (Print: docs, language switch)

Everything is in namespace JavaFarmer.Compiler (netstandard2.1, no game references). This document is the contract
between frontends, printers and the shared emitter. The code (Ir.cs, IrTypes.cs, IrEmitter.cs) is the final word;
if this file and the code disagree, fix the file.

## 1. Ground rules

* **Frontends do the language, the emitter does Python.** A frontend resolves names, checks types, reports errors in
  its language's wording and makes every language-specific semantic decision explicit in the IR (which division,
  which modulo, string concatenation vs addition, default values, 1-based indexing, ...). The emitter never looks
  at source text and never guesses a language.
* **The IR is checked.** IrEmitter.Compile expects a module the frontend accepted. It still reports a few
  structural errors (a ?: it cannot hoist, ListSet used as a value, an inclusive range with a variable step) as
  Diagnostics at the node's Span, so give every node a Span.
* **Identity matters.** IrSymbol objects identify variables; two locals named "i" in different scopes are two
  symbols. IR nodes must not be shared between two places in the tree (build a new node each time).
* **No parentheses nodes.** Precedence is structural. IrExpr.ParenSpan records the outermost source parentheses
  (printers may keep them; the emitter maps the parentheses Python needs to that span).
* **Comments** (IrComment: Text without markers, IsBlock, IsDoc, Span, BlankLinesBefore) hang on nodes:
  IrNode.Leading (own lines before the node), IrNode.Trailing (same line, after it), IrBlock.TrailingComments
  (after the last statement of a block), IrModule.TrailingComments (end of file). Frontends whose parser drops
  comments can call IrComments.Attach(module, comments, source) which places them by span. The emitter ignores
  comments; printers must print them.
* **Layout hints**: IrNode.BlankLinesBefore / IrComment.BlankLinesBefore (empty lines before), optional.

## 2. Spans and the source map

Every node has Span (whole construct, source offsets, End exclusive; Span.None = (-1,-1)). Nodes also carry
sub-spans the emitter maps specific Python text to:

| node | sub-spans | used for |
|---|---|---|
| IrFunction | HeaderSpan (modifiers .. ')'), NameSpan | "def name(...):" line; entry: whole def/guard = scaffolding |
| IrParam | NameSpan | parameter names in the def line; entry params: "args = []" scaffolding (Span) |
| IrGlobal / IrVarDecl | NameSpan | the assignment target |
| IrAssign | OpSpan | " = ", " += ", the "1" of x++ (give the literal 1 the OpSpan too) |
| IrIf / IrWhile / IrLoop / IrClassicFor / IrRangeFor / IrForEach / IrSwitch | KwSpan | keyword lines, "pass" of empty bodies |
| IrDoWhile | KwSpan ('do'), WhileSpan ('while') | the loop line / the "if not c: break" check (Structural) |
| IrClassicFor / IrRangeFor | HeaderSpan ('for' .. ')') | range(...) text, the "True" of an empty condition |
| IrRangeFor / IrForEach | VarSpan (+ ValueVarSpan) | the loop variable |
| IrCase | LabelSpan ("case X:" / "X =>") | elif/else lines |
| IrBinary / IrUnary | OpSpan | the operator; also helper calls (__jdiv ...) made for it |
| IrConditional | QSpan ('?') | the "if c:" line of the hoisted if/else |
| IrCall / IrOpCall | NameSpan (+ QualifierSpan for Module.f) | function name, ".append" ...; Span for the parentheses |
| IrName (other module's global) / IrFuncRef | QualifierSpan, NameSpan | "Module" and "name" of Module.name |
| IrCast | OpSpan | "(int)", "String.valueOf", "as i32" |
| IrLambda | HeaderSpan ("() ->", "||", "function()") | the hoisted def line |
| IrBlock | Span covering the braces / body | "pass" of an empty function body = last char of the span |

A missing span is allowed: the emitter tags that text Synthetic with (-1,-1).

**Segment kinds are derived by the emitter** (SPEC §7), not set by frontends:

* User: ordinary code (charges the game's unlock dependencies).
* Synthetic: emitter scaffolding (helpers, temps, str() for concatenation, global lines, the def/guard of the entry).
  Entry function header, its __name__ guard and global lines also get NoHighlight.
* Structural: the init/condition/update of a classic for lowered to while (non-call nodes charge nothing) and the
  "True"/"+ 1" pieces of loops; the while made from a classic for has DependencyOverride "for".
* **IrNode.Synthetic = true** is the frontend's only lever: the node was invented by the frontend (C's printf
  lowering adds an IrCast(String) and an IrBinary Concat; R's seq lowering adds arithmetic; a frontend-built
  return 0 is better dropped). The node's own text becomes Synthetic (free); its children keep their own flag.
  Mandatory structure (main headers, #include, fn main, package lines) has no IR node at all, or is the entry
  function (whose header is scaffolding automatically).

## 3. Module

IrModule { Name, Items, TrailingComments, SourceLanguage }. Items in source order, each one of:

* IrFunction { Symbol (Kind Function), Params (IrParam { Symbol, Default, NameSpan }), ReturnType, Body, IsEntry,
  IsPrivate, HeaderSpan, NameSpan }
* IrGlobal { Symbol (Kind Global), Init (null → emitter writes 0 / False / None by Symbol.Type), ImplicitInit
  (frontend supplied a default because the source had none; printers may omit it), NameSpan, TypeExplicit, IsPrivate }
* IrStmt: a top-level statement (script code: Python, JS, R). IrImport is only legal here or in functions.

Emission order:

* Module **without** top-level statements: helpers, imports, functions (except the entry) in order, globals in
  order (so initialisers may call functions), the entry as "def main():", then the guard
  "if __name__ == \"__main__\":\n    main()". Imported modules get __name__ = their name, so a library's main does
  not run on import.
* **Script** module (IsScript: has IrStmt items): helpers, imports, then every item in order (functions as defs,
  globals as assignments, statements as statements); an entry function (if any) still gets the guard at the end.
* Imports: every other module referenced through External* symbols gets "import Module" at the top, mapped to its
  first use (QualifierSpan), unless the module already has an explicit IrImport for it.

Entry function: IsEntry = true for main (Java "void main()", C "int main(void)", Rust "fn main()", C#
"static void Main()", Zig "pub fn main() void"). Its params are bound to empty lists (Java's String[] args).
Frontends drop a trailing "return 0;" of C/C++ main.

## 4. Symbols and names

IrSymbol { Name (source spelling), Kind, Type, IsConst, Module, Function, ParamTypes, DeclSpan, PyName, Tag }.

| Kind | meaning | emitted as |
|---|---|---|
| Local | function local, or script-level local of a top-level script | its Python name |
| Param | parameter (function or lambda) | its Python name |
| Global | module-level variable of this module | its Python name; functions assigning it get "global x" |
| Function | function of this module (Function = declaration; may be null for an undefined name in a docs snippet) | its Python name |
| ExternalGlobal / ExternalFunction | another module's variable / function (Module = file name) | Module.name |

Python names (PyNaming / the emitter's naming pass, identical to v1):
* reserved names (Python keywords, game builtins, constants, append/add/remove/pop/insert, __name__ ...) get a
  "_j" suffix: list → list_j; identifiers that do not start with [A-Za-z_] get a "j_" prefix;
* a global with the same source name as a function gets "_f";
* a local/param whose Python name equals a module-level name (function, global, this module, "main", other
  modules) gets "_l";
* a local that shadows a different, still-live local of an enclosing block (C/Rust/JS shadowing) gets "_2", "_3" ...;
* the "__j" prefix is reserved for emitter helpers and temps: frontends must reject user identifiers starting with it;
* external symbols use IrSymbol.PyName when the frontend sets it (it should: compute it with
  PyNaming.ModuleLevel(functionNames, globalNames, out globals) over the other module's declarations), else Ident(name).
CompileResult.Names maps every renamed Python identifier back to the source name ("list_j" → "list",
"__jlambda1" → "lambda").

The emitter adds "global g" lines (Scaffold, mapped to the first assignment) to every function and lambda that
assigns (=, op=, unpacking, loop variable) a Global of this module. Frontends never emit global declarations.

## 5. Statements

| node | semantics | emitted Python |
|---|---|---|
| IrBlock { Stmts, TrailingComments } | nested scope (braces) | its statements inline; empty bodies get "pass" |
| IrVarDecl { Symbol, Init, NameSpan, TypeExplicit } | declares a local | "x = init"; nothing when Init is null (a later assignment initialises it) |
| IrAssign { Target, Op?, Value, OpSpan, TruncateToInt, IsIncDec, Prefix } | target = value / target op= value | see below |
| IrExprStmt { Expr } | expression for its effect | the call; a non-call value becomes "__jv = value" |
| IrIf { Cond, Then, Else (IrBlock / IrIf / null), KwSpan } | if / else if / else | if/elif/else chain (all conditions evaluated before the chain for hoisting) |
| IrWhile { Cond, Body } | while | "while c:" |
| IrDoWhile { Body, Cond } | do { } while (c) | "while True: body; if not (c): break"; continue re-checks c first |
| IrLoop { Body } | infinite loop (Rust loop, R repeat) | "while True:" |
| IrClassicFor { Init, Cond?, Update, Body, HeaderSpan } | C-style for | range() when the pattern matches (below), else init; "while c:" (DependencyOverride "for", Structural header); update at the end of the body and before every continue |
| IrRangeFor { Var, Start?, End, Step?, Inclusive } | counting loop over [Start, End) (Inclusive: [Start, End]); Start null = 0, Step null = 1 | "for v in range(...)"; Inclusive adds "+ 1"/"- 1" and requires a literal step |
| IrForEach { Var, ValueVar?, Iterable } | iterate elements (lists, arrays, sets, strings) / keys (maps); ValueVar = map value | "for k in it:" (+ "v = it[k]"); MapKeys iterable iterates the map directly |
| IrSwitch { Selector, Cases, Style } / IrCase { Labels, IsDefault, Body } | no fallthrough: each case is a branch; several labels = or; default matches the rest | selector evaluated once (temp "__jsN" unless a plain name/literal), if/elif/else chain |
| IrBreak / IrContinue | innermost loop (frontends drop the break that ends a switch case and reject breaks that would leave a switch for an outer loop) | break / continue |
| IrReturn { Value? } | return | "return v" (a ?: value becomes if/else returns) |
| IrImport { Module, Names?, ImportAll } | explicit import (Python "import X"; "from X import a, b" sets Names, "from X import *" sets ImportAll; the names are still bound as External symbols of X) | "import Module" |

Range-for detection (IrClassicFor → range, exactly v1): Init is one IrVarDecl of an Int symbol with an Int
initialiser; Cond is IrBinary Lt/Le/Gt/Ge with Left = that symbol and an Int Right; Update is one IrAssign
(Add/Sub of a positive Int literal, no TruncateToInt) on the symbol; the direction matches the comparison; the
body never assigns the symbol; Right is loop-invariant: Int literals, Neg/Add/Sub/Mul of invariants, a
local/param the body does not assign, a const global (IsConst), a LoopInvariant builtin (get_world_size,
max_drones), Len of a string/array local the body does not assign, or Len of a list local the body only reads
(Index / Len / IsEmpty / Contains / ListIndexOf / ==, !=, str()).

Assignments:
* Op null: "t = v". A ?: value is lowered to if/else assignments (via a temp when the target is not a plain local/global).
* Op Concat: "t += v" (str() around a non-string value).
* Op Add/Sub/Mul/TrueDiv/ModFloor without TruncateToInt: "t op= v".
* Op IntDivTrunc → "t = __jdiv(t, v)", ModTrunc → "t = __jmod(t, v)", Pow → "t = __jpow(t, v)", IntDivFloor → "t = t // v";
  TruncateToInt wraps the result in __jtrunc (Java/C/C# "int x; x += 1.5"). These forms need a side-effect free
  target (a name, or an index whose parts have no calls).
* Target IrTupleLit: unpacking "a, b = value".

## 6. Expressions

| node | semantics | emitted Python |
|---|---|---|
| IrLiteral { Kind Int/Float/Bool/String/Null } | literal; Str holds the decoded value; the game has no escapes, so a string must not contain a line break | 1, 1.5 (always with '.', never exponent), True, None, "s" / 's' / ("a" + 'b') when both quotes occur |
| IrName { Symbol } | variable / other module's global / a function used as a value | name, Module.name |
| IrConst { Category, Name } | game constant: "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards", or "Directions" (North ...) | Entities.Carrot, North. Never hard-code member lists: any member name is accepted |
| IrBinary { Op, Left, Right } | see §7 | |
| IrUnary { Neg, Plus, Not } | | -x, +x, not x |
| IrConditional { Cond, Then, Else } | c ? a : b | hoisted into if/else when it is a whole value: a declaration/assignment value, a return value, a whole argument / collection item whose preceding arguments have no side effects (not inside and/or); anything else is a Diagnostic "?: is only supported as a whole value here" |
| IrCall Builtin { Builtin, Args } | game builtin by canonical Python name (GameApi.Find); print() with no args prints an empty line | name(args) |
| IrCall Function { Function, Args } | user / other-module function | f(args), Module.f(args) |
| IrCall Value { Callee, Args } | call a function value (Runnable.run(), a closure in a variable, a param) | callee(args) |
| IrOpCall { Op, Args } | canonical operation, receiver first (§8) | |
| IrIndex { Target, Index, BaseOffset } | element (list, array, string, map value, tuple item); BaseOffset 1 for 1-based sources (R) | t[i] / t[i - 1] |
| IrListLit { Items, IsArray } / IrSetLit / IrMapLit { Keys, Values } / IrTupleLit | literals (IsArray: fixed-size array literal) | [..], set() / {..}, {k: v}, (a, b) |
| IrLambda { Params, Body / BodyExpr, ReturnsValue, ReturnType } | closure over enclosing locals by reference (Python semantics) | hoisted "def __jlambdaN(params):" right before the statement that contains it (its own global lines); the lambda value is the name |
| IrFuncRef { Function / Builtin } | function value (method reference, function pointer) | name / Module.name |
| IrCast { Target, Operand, IsTypeAssertion } | Int: truncate a float toward 0 (no-op on ints); Float: no-op; String: str(x); IsTypeAssertion (reference casts): no code | "x // 1" when x is provably >= 0, else __jtrunc(x); str(x) |

Side effects for hoisting: a call is "impure" unless it is min/max/abs, a cast, Len of a list/array/string,
Contains on a map, MapGetOrNull, Floor/Ceil/Round/Sqrt or a Position/Companion op (v1 rules).

## 7. Numeric semantics

The game has one number type (double). Int vs Float exists for checking and printing only; integer overflow is
not emulated. Frontends pick the operator that matches their language's meaning:

| IrBinOp | meaning | sources | emitted |
|---|---|---|---|
| Add, Sub, Mul | arithmetic (Add on unknown types = Python +) | all | + - * |
| TrueDiv | real division | Python /, JS /, R /, Java/C double / | / |
| IntDivTrunc | integer division toward 0 | Java/C/C++/C#/Rust/Zig int / | "a // b" when both are provably >= 0, else __jdiv(a, b) |
| IntDivFloor | integer division rounding down | Python //, R %/%, Java Math.floorDiv, Zig @divFloor | // |
| ModTrunc | remainder with the dividend's sign (also on floats) | Java/C/C++/C#/Rust/JS %, Zig @rem | "a % b" when both >= 0, else __jmod(a, b) |
| ModFloor | modulo with the divisor's sign | Python %, R %%, Math.floorMod, Zig @mod | % |
| Pow | power | Python **, JS **, R ^, Math.pow, f64.powf | __jpow(a, b) |
| Eq Ne Lt Le Gt Ge | comparison (Eq on strings is value equality) | | == != < <= > >= |
| And Or | short-circuit | | and / or |
| Concat | string concatenation; a non-string operand is converted with str() (str() is skipped for string literals, Concat results, str() casts and string indexing) | Java/C#/JS + on strings, Rust format!, C printf lowering, R paste0 | + |

"Provably >= 0": non-negative literals; get_pos_x/get_pos_y/get_world_size/num_drones/max_drones/num_items/
get_time/get_tick_count/get_water/num_unlocked/abs/random (GameApi NonNegative flag); Len; Sqrt; min of
non-negatives; max with a non-negative argument; Floor/Ceil/Round of a non-negative; Add/Mul/divisions/modulos of
non-negatives; casts of non-negatives; the variable of a range loop counting up from a non-negative start.

Integer conversion: IrCast(Int) truncates toward 0; Floor/Ceil/Round are explicit IrOps (Round = floor(x + 0.5)).

## 8. Collection / string / math operation catalogue (IrOp)

Receiver first. "stmt only" ops are not values.

| IrOp | args | meaning | emitted |
|---|---|---|---|
| Len | c | size of list/array/set/map/string | len(c) |
| IsEmpty | c | size == 0 | len(c) == 0 |
| Contains | c, x | membership (map: key; string: substring) | x in c |
| ListAppend | l, x | add at the end | l.append(x) |
| ListInsert | l, i, x | insert before i | l.insert(i, x) |
| ListPop | l [, i] | remove and return the last / i-th element | l.pop() / l.pop(i) |
| ListSet | l, i, x | stmt only: l[i] = x (Java list.set) | l[i] = x |
| ListIndexOf | l, x | first index or -1 | __jindexof(l, x) |
| RemoveValue | c, x | remove x, error when missing (Python list/set.remove) | c.remove(x) |
| RemoveValueIfPresent | c, x | remove x if present (Java remove(Object)) | __jremove(c, x) |
| Clear | c | empty list/set/map in place (receiver type decides) | __jlclear / __jsclear / __jmclear |
| SetAdd | s, x | add | s.add(x) |
| MapPut | m, k, v | stmt only: m[k] = v | m[k] = v |
| MapGetOrNull | m, k | value or null | __jget(m, k) |
| MapGetOrDefault | m, k, d | value or d | __jgetd(m, k, d) |
| MapRemove | m, k | remove and return, error when missing (Python dict.pop) | m.pop(k) |
| MapRemoveIfPresent | m, k | remove and return, null when missing (Java) | __jmremove(m, k) |
| MapKeys | m | key set; as a for-each iterable it iterates m directly | set(m) |
| MapValues | m | list of values | __jvalues(m) |
| ListCopy / SetCopy / MapCopy | c | shallow copy / conversion | list(c) / set(c) / dict(c) |
| ArrayNew | n, d | n copies of d (new int[n]) | __jarr(n, d) |
| ArrayNew2 | w, h, d | w lists of h copies of d | __jarr2(w, h, d) |
| Floor, Ceil, Round, Sqrt | x | math | __jfloor / __jceil / __jround / __jsqrt |
| PosX, PosY | p | x / y of a Position (x, y) tuple | p[0] / p[1] |
| CompanionEntity, CompanionPos | c | parts of (entity, (x, y)) | c[0] / c[1] |

Element access is IrIndex (list.get(i), s.charAt(i), m[k] when the key must exist). Plain m[k] = v and l[i] = v
are IrAssign with an IrIndex target; ListSet/MapPut exist so Java's set/put statements keep their v1 analysis.
Python methods map to ops (append, insert, pop, remove, add), never to IrCall. Helpers (__j*) are written in the
basic Python the game supports and are emitted once per module, only when used.

Game builtins are IrCall Builtin with the canonical snake_case name from GameApi (§10), including print, quick_print,
min, max, abs, random. len/str/list/set/dict/range are not builtin calls in the IR (Len, IrCast, copies, IrRangeFor).

## 9. Types (IrTypes.cs)

IrType { Kind, Elem, Key, GameName, Items, Nullable } with Kind: Unknown, Any (Java Object), Void, Null, Int, Float,
Bool, String, List, Array (fixed size), Set, Map (Key → Elem), Tuple (Items), Game (Entity, Ground, Item, Hat,
Unlock, Leaderboard, Direction, Drone, Position, Companion), Func (Items params → Elem return). Nullable marks values
that may be null (get_entity_type, get_cost, get_companion, spawn_drone). Neutral spelling (ToString/Parse):
int, float, bool, string, void, any, list<int>, array<int>, set<Item>, map<Item,float>, tuple<int,int>, Entity?,
func, func<int>.

Static frontends set IrExpr.Type on every expression and IrSymbol.Type on every symbol. The emitter reads types for:
range detection (Int), Concat (String), IrCast (operand Float/Unknown → truncation), Clear (receiver kind),
OnlyReads/IsPureCall (receiver kind), global defaults (Bool/numeric/other).

**Inference contract (IrTypeInference.Infer(module), for Python/JS/R and docs translation).** Fills Unknown types
only, never overwrites known ones: literals; symbol = IrType.Join over all assignments (Int+Float = Float, Null+T =
T?, different kinds = Any); arithmetic (Int op Int = Int except TrueDiv = Float; any Float = Float; a String side of
Add/Concat = String); comparisons/Contains/IsEmpty/And/Or/Not = Bool; builtins from GameApi (NumericLikeArgs:
Int when all args are Int); user calls = join of the function's returns (Void if none); params = join of argument
types at call sites; literals = joined item types; empty collections are refined by later appends/adds/puts/index
assignments on the same symbol; Index of list/array = element, of map = value, of string = String, of tuple with a
literal index = that item; ForEach var = element (map: key); RangeFor var = Int; Len = Int; MapGet* = value type;
IrConst = its game type; IrCast = target. What stays Unknown is the printer's problem: use the language's escape
hatch (var/auto/let/Object/any), a "TODO type" comment only as a last resort.

IrDeclarations.Place(module) turns the first plain assignment of each Local into an IrVarDecl where that is
valid for block-scoped languages, or inserts a declaration without value at the start of the smallest block that
contains all uses (dynamic sources only; static frontends declare locals themselves).

## 10. Game API (Core/GameApi.cs)

GameApi.Builtins: ApiBuiltin { PythonName, Params (ApiParam { Name, Type, TypeText, Optional, Variadic }),
ReturnType/ReturnText, Doc, Extension, NonNegative, LoopInvariant, Pure, NumericLikeArgs, MinArgs, MaxArgs,
Signature }. Find(name), ApiName(pythonName, NamingStyle Snake/Camel/Pascal), FromApiName, ToStyle,
ConstantCategories, DirectionNames, GameTypeOfCategory / CategoryOfGameType.
Extensions register builtins at runtime:

    GameApi.Register("scan_port", "host: string, port?: int", "bool", "Scans a port.", extension: "cybersecurity");
    GameApi.Unregister("scan_port");     // GameApi.Changed / Version tell languages to refresh caches

Signature syntax: "name: type" comma separated, "name?: type" (or "= default") optional, "*name: type" variadic;
types use the neutral spelling, unions "Entity|Item" allowed (typed Any). Every language sees registered builtins
under its own naming (Java: camelCase via the Java view GameApi.Java.cs, which also builds overloads for optional
parameters). Frontends must accept any registered builtin: look names up in GameApi at compile time, never cache
the table across GameApi.Version changes.

## 11. Things the IR deliberately supports for other languages

* C: no closures. IrLambda bodies can be hoisted to top-level functions when IrWalk.Captures(lambda) is empty;
  spawn_drone takes an IrFuncRef (function pointer). Fixed arrays = IrTypeKind.Array + ArrayNew/IrListLit IsArray.
  printf lowering = Concat/IrCast nodes marked Synthetic.
* Rust: Option = Nullable types; let mut = !IsConst; match = IrSwitch (Style Match) or IrConditional chains;
  a..b / a..=b = IrRangeFor (Inclusive); loop = IrLoop.
* Zig: namespaced farm.harvest() = IrCall Builtin (naming is the printer's business); enum literals .North =
  IrConst("Directions", "North"); @divTrunc/@divFloor/@rem/@mod map 1:1 to the IrBinOps.
* R: 1-based indexing = IrIndex.BaseOffset 1; top-level scripts = IrStmt items; "<-" is just IrAssign;
  for (i in 1:n) = IrRangeFor Inclusive; repeat = IrLoop; %/% and %% = IntDivFloor/ModFloor.
* JS: dynamic types (Unknown + IrTypeInference); numbers are doubles like the game; Map/Set/Array map to the ops.
* C#: PascalCase API names (NamingStyle.Pascal); otherwise close to Java.
* C++: lambdas [&]{} = IrLambda; std::vector/map/set map to the ops; range-for = IrForEach.

## 12. Utilities

* IrTypeInference.Infer(module) / IrDeclarations.Place(module) (§9) — the Python frontend runs both inside ToIr.
* DocsTranslator.Translate(markdown, target[, out failures, DocsTranslateOptions]) and DocsTranslator.Spans(markdown):
  rewrites the code spans of a game docs page (blocks: Python ToIr → target Print in Snippet mode; inline spans: names,
  keywords, constants, values). Untranslatable spans stay as they are and are reported "#<index>: reason".
* IPrinterDiagnostics (optional on an ILanguage): Print(module, options, out problems) — lets DocsTranslator and the
  language switch keep the original text instead of showing a wrong translation. JavaLanguage implements it.


IrWalk.Children / Descendants (pre-order, optionally not into lambdas), AssignedSymbols, Assigns, Captures(lambda),
ReferencedModules. IrComments.Attach. PyNaming. PyFormat.Double (plain decimal). PrinterBase (indentation, comments,
precedence helpers, string escaping, Camel/Pascal/Snake). LanguageBase (Compile = ToIr + IrEmitter.Compile,
exception safety, ApiName by NamingStyle, FormatValue via ValueFormat). CompileResults.Fail/Internal.
