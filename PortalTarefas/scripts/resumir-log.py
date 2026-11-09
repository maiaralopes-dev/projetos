"""Resume o log do console simples: mantém hora, nível, categoria curta, CorrelationId e mensagem."""
import re, sys
for linha in open(sys.argv[1], encoding="utf-8"):
    m = re.match(r"(\d\d:\d\d:\d\d) (\w+): ([\w.]+)\[\d+\] (.*)", linha.rstrip())
    if not m:
        continue
    hora, nivel, cat, resto = m.groups()
    corr = re.search(r"CorrelationId:(\S+)", resto)
    msg = resto.split(" => ")[-1]
    msg = re.sub(r"^CorrelationId:\S+ ", "", msg)
    print(f"{hora} {nivel:4} {cat.split('.')[-1]:<28} corr={corr.group(1)[:12] if corr else '-':<12} {msg}")
