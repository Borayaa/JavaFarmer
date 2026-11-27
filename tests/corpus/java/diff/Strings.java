// String literals with quotes and backslashes, concatenation with null/boolean.
void main() {
    String q = "say \"hi\"";
    String a = "it's";
    String both = "\"it's\"";
    String tab = "a\tb";
    String slash = "c:\\farm";
    System.out.println(q);
    System.out.println(a);
    System.out.println(both);
    System.out.println(tab);
    System.out.println(slash);
    String s = null;
    System.out.println("value: " + s);
    System.out.println("flag: " + (1 > 0));
    String acc = "";
    for (int i = 0; i < 3; i++) acc += i;
    acc += "!";
    System.out.println(acc);
    System.out.print("no newline");
    System.out.println();
}
