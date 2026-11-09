import csv, sys
from collections import defaultdict
entrada, saida = sys.argv[1], sys.argv[2]
dados = defaultdict(list)
for l in csv.DictReader(open(entrada)):
    dados[l["cenario"]].append((float(l["t_ms"])/1000, int(l["profundidade"])))
nomes = {"A":"A · base (cap 200, 4 cons., banco 1, Wait 100 ms)","B":"B · mais paralelismo (cap 200, 16 cons., banco 8)",
         "C":"C · fila grande (cap 10.000, 4 cons., banco 1)","D":"D · DropOldest (cap 200, 4 cons., banco 1)",
         "E":"E · rejeição imediata (cap 200, 4 cons., banco 1, Wait 0)","F":"F · A + lote de até 32 itens por transação (extensão)"}
cap = {"A":200,"B":200,"C":10000,"D":200,"E":200,"F":200}
W, PH, ML, MR, MT, GAP = 760, 120, 64, 24, 78, 46
H = MT + 6*PH + 5*GAP + 60
tmax = max(t for s in dados.values() for t,_ in s)
cor, tinta, tinta2, grade, fundo = "#2a78d6", "#0b0b0b", "#52514e", "#e4e3df", "#fcfcfb"
o = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" font-family="Segoe UI, Arial, sans-serif">',
     f'<rect width="{W}" height="{H}" fill="{fundo}"/>',
     f'<text x="{ML}" y="24" font-size="16" font-weight="600" fill="{tinta}">Profundidade da fila sob carga de 1.500 pedidos/s (rodada 1)</text>',
     f'<text x="{ML}" y="42" font-size="12" fill="{tinta2}">Itens aguardando, amostrados a cada 100 ms. Cada painel tem sua própria escala vertical;</text>',
     f'<text x="{ML}" y="57" font-size="12" fill="{tinta2}">a linha tracejada é a capacidade da fila (em C, 10.000, nunca atingida).</text>']
def nice(v):
    for n in [50,100,200,250,500,1000,2000,2500,5000,10000]:
        if v/n <= 4: return n
    return 10000
for i, k in enumerate("ABCDEF"):
    y0 = MT + i*(PH+GAP) + 14
    serie = dados[k]
    ymax = max(max(p for _,p in serie), cap[k] if cap[k] < 1000 else 0)
    passo = nice(ymax); topo = passo * -(-ymax // passo)
    if cap[k] > ymax and topo - ymax < 0.12 * topo: topo += passo  # folga para o rótulo do pico
    X = lambda t: ML + (W-ML-MR) * t / tmax
    Y = lambda v: y0 + PH - PH * v / topo
    o.append(f'<text x="{ML}" y="{y0-6}" font-size="13" font-weight="600" fill="{tinta}">{nomes[k]}</text>')
    v = 0
    while v <= topo:
        o.append(f'<line x1="{ML}" x2="{W-MR}" y1="{Y(v):.1f}" y2="{Y(v):.1f}" stroke="{grade}" stroke-width="1"/>')
        o.append(f'<text x="{ML-8}" y="{Y(v)+4:.1f}" font-size="11" fill="{tinta2}" text-anchor="end">{v:,}</text>'.replace(",", "."))
        v += passo
    if cap[k] <= topo:
        o.append(f'<line x1="{ML}" x2="{W-MR}" y1="{Y(cap[k]):.1f}" y2="{Y(cap[k]):.1f}" stroke="{tinta2}" stroke-width="1" stroke-dasharray="4 3"/>')
        o.append(f'<text x="{W-MR}" y="{Y(cap[k])-4:.1f}" font-size="11" fill="{tinta2}" text-anchor="end">capacidade {cap[k]}</text>')
    pts = " ".join(f"{X(t):.1f},{Y(p):.1f}" for t,p in serie)
    o.append(f'<polyline points="{pts}" fill="none" stroke="{cor}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>')
    pico = max(serie, key=lambda s: s[1])
    if cap[k] > topo: o.append(f'<text x="{X(pico[0]):.1f}" y="{Y(pico[1])-8:.1f}" font-size="11" fill="{tinta}" text-anchor="middle">máx. {pico[1]:,}</text>'.replace(",", "."))
yb = MT + 5*(PH+GAP) + 14 + PH
t = 0
while t <= tmax:
    x = ML + (W-ML-MR) * t / tmax
    o.append(f'<text x="{x:.1f}" y="{yb+16}" font-size="11" fill="{tinta2}" text-anchor="middle">{t:.0f} s</text>'); t += 2
o.append(f'<text x="{ML + (W-ML-MR)/2}" y="{yb+36}" font-size="11" fill="{tinta2}" text-anchor="middle">tempo desde o início da carga (oferta encerra em 6 s; depois a fila só drena)</text>')
o.append('</svg>')
open(saida, "w").write("\n".join(o))
