"""Resume o CSV do dotnet-counters numa linha do tempo (1 linha a cada N segundos).
Uso: python3 resumir_counters.py counters.csv [passo_s] [inicio_hh:mm:ss] [fim_hh:mm:ss]"""
import csv, sys
from collections import defaultdict

arquivo = sys.argv[1]
passo = int(sys.argv[2]) if len(sys.argv) > 2 else 2
ini = sys.argv[3] if len(sys.argv) > 3 else None
fim = sys.argv[4] if len(sys.argv) > 4 else None

series = defaultdict(dict)  # nome -> {hh:mm:ss: valor}
for l in csv.DictReader(open(arquivo)):
    t = l["Timestamp"].split(" ")[1]
    if (ini and t < ini) or (fim and t > fim):
        continue
    try:
        series[l["Counter Name"]][t] = float(l["Mean/Increment"])
    except ValueError:
        pass

def s(prefixo):
    return next((v for k, v in series.items() if k.startswith(prefixo)), {})

def soma(*prefixos):
    out = defaultdict(float)
    for p in prefixos:
        for k, v in series.items():
            if k.startswith(p):
                for t, x in v.items(): out[t] += x
    return out

# No dotnet-counters 10, contadores "UpDown" (threads e fila do ThreadPool) chegam como
# VARIAÇÃO por intervalo; acumulamos para obter o nível relativo ao início da coleta.
def nivel(prefixo):
    serie, acc, out = s(prefixo), 0.0, {}
    for t in sorted(serie):
        acc += serie[t]; out[t] = acc
    return out
threads_tp, fila_tp = nivel("dotnet.thread_pool.thread.count"), nivel("dotnet.thread_pool.queue.length")

cpus = max(s("dotnet.process.cpu.count").values() or [1])
cpu = soma("dotnet.process.cpu.time")
colunas = [
    ("CPU %", lambda t: cpu.get(t, 0) / cpus * 100, "{:5.0f}"),
    ("Threads TP (nível)", lambda t: threads_tp.get(t), "{:4.0f}"),
    ("Fila TP (nível)", lambda t: fila_tp.get(t), "{:5.0f}"),
    ("Itens TP/s", lambda t: s("dotnet.thread_pool.work_item.count").get(t), "{:6.0f}"),
    ("Lock cont./s", lambda t: s("dotnet.monitor.lock_contentions").get(t), "{:5.0f}"),
    ("GC gen0/s", lambda t: s("dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen0]").get(t), "{:4.0f}"),
    ("Aloc. MB/s", lambda t: (s("dotnet.gc.heap.total_allocated").get(t) or 0) / 1e6, "{:6.1f}"),
    ("Heap gen0+1+2 MB", lambda t: sum(series.get(f"dotnet.gc.last_collection.heap.size (By)[gc.heap.generation={g}]", {}).get(t, 0) for g in ("gen0","gen1","gen2")) / 1e6, "{:6.1f}"),
    ("Req. ativas GET", lambda t: s("http.server.active_requests ({request})[http.request.method=GET").get(t), "{:5.0f}"),
    ("Req. ativas POST", lambda t: s("http.server.active_requests ({request})[http.request.method=POST").get(t), "{:5.0f}"),
    ("Exceções/s", lambda t: s("dotnet.exceptions").get(t, 0), "{:4.0f}"),
]
tempos = sorted(set(t for v in series.values() for t in v))
print("| Hora | " + " | ".join(c[0] for c in colunas) + " |")
print("|---|" + "---:|" * len(colunas))
for t in tempos[::passo]:
    vals = []
    for _, f, fmt in colunas:
        v = f(t)
        vals.append("—" if v is None else fmt.format(v).strip())
    print(f"| {t} | " + " | ".join(vals) + " |")

# Resumo da janela com carga (segundos com > 300 itens/s no ThreadPool).
ativos = [t for t in tempos if (s("dotnet.thread_pool.work_item.count").get(t) or 0) > 300]
if ativos:
    media = lambda f: sum((f(t) or 0) for t in ativos) / len(ativos)
    print()
    print(f"Resumo com carga ({len(ativos)} s): CPU média {media(lambda t: cpu.get(t, 0) / cpus * 100):.0f}% | "
          f"threads TP máx {max(threads_tp.get(t, 0) for t in ativos):.0f} | itens TP/s médio {media(lambda t: s('dotnet.thread_pool.work_item.count').get(t)):.0f} | "
          f"alocação média {media(lambda t: (s('dotnet.gc.heap.total_allocated').get(t) or 0) / 1e6):.1f} MB/s | "
          f"GC gen0 {media(lambda t: s('dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen0]').get(t)):.1f}/s | "
          f"contenções de lock {media(lambda t: s('dotnet.monitor.lock_contentions').get(t)):.1f}/s | "
          f"POST ativas média {media(lambda t: s('http.server.active_requests ({request})[http.request.method=POST').get(t)):.1f}")
