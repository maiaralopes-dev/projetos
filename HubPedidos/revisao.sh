#!/usr/bin/env bash
# Revisão automatizada do Hub de Pedidos — o MESMO script roda local e no CI.
# Falha cedo, diz QUAL etapa falhou e deixa resultados legíveis em artifacts/.
#   ./revisao.sh            # tudo
# Etapas: restore → build (analisadores) → formatação → testes (TRX + cobertura) → resumo.
set -uo pipefail
cd "$(dirname "$0")"
ART=artifacts
rm -rf "$ART" && mkdir -p "$ART/testes"
RESUMO="$ART/resumo.md"
inicio=$(date +%s)

etapa() { echo; echo "══════ [$1] $2"; }
falhou() {
  echo; echo "✖ REVISÃO REPROVADA na etapa: $1"; echo "  Motivo: $2"
  { echo "## ✖ Revisão reprovada — etapa: $1"; echo; echo "$2"; } >> "$RESUMO"
  [ -n "${GITHUB_STEP_SUMMARY:-}" ] && cat "$RESUMO" >> "$GITHUB_STEP_SUMMARY"
  exit 1
}

etapa 1/5 "Restaurar ferramentas e pacotes"
dotnet tool restore > "$ART/restore.log" 2>&1 && dotnet restore HubPedidos.slnx >> "$ART/restore.log" 2>&1 \
  || falhou "restore" "veja $ART/restore.log"

etapa 2/5 "Build Release com analisadores (regras elevadas a erro bloqueiam)"
if ! dotnet build HubPedidos.slnx -c Release --no-restore -nologo > "$ART/build.log" 2>&1; then
  # Remove o sufixo "[projeto.csproj]" e o caminho absoluto; o MSBuild repete cada erro, daí o sort -u.
  grep -E " error " "$ART/build.log" | sed -E 's| \[[^]]*\]$||' | sed "s|$PWD/||" | sort -u > "$ART/erros-build.txt"
  head -20 "$ART/erros-build.txt"
  falhou "build/analisadores" "$(wc -l < "$ART/erros-build.txt") erro(s) distinto(s); detalhes em $ART/build.log"
fi
avisos=$(grep -E " warning " "$ART/build.log" | sort -u | wc -l)
echo "ok (avisos não bloqueantes: $avisos)"

etapa 3/5 "Formatação e estilo (dotnet format --verify-no-changes)"
if ! dotnet format HubPedidos.slnx --verify-no-changes --no-restore --verbosity minimal > "$ART/format.log" 2>&1; then
  grep -E "error|warning" "$ART/format.log" | sed -E 's| \[[^]]*\]$||' | sed "s|$PWD/||" | head -20
  falhou "formatação" "rode 'dotnet format HubPedidos.slnx' e versione o resultado; detalhes em $ART/format.log"
fi
echo ok

etapa 4/5 "Testes (ordem aleatória, TRX e cobertura)"
dotnet test HubPedidos.slnx -c Release --no-build --nologo \
  --logger "trx;LogFileName=resultados.trx" --logger "console;verbosity=minimal" \
  --collect "XPlat Code Coverage" --results-directory "$ART/testes" > "$ART/testes.log" 2>&1
codigo_testes=$?
grep -E "ordem-aleatoria|Passed!|Failed!" "$ART/testes.log" | sed 's/^\[xUnit[^]]*\] //'

etapa 5/5 "Resumo"
python3 - "$ART" "$RESUMO" "$(( $(date +%s) - inicio ))" <<'PY'
import glob, sys, xml.etree.ElementTree as ET
art, resumo, segundos = sys.argv[1], sys.argv[2], sys.argv[3]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
trx = glob.glob(f"{art}/testes/**/*.trx", recursive=True)
total = ok = falhas = 0; linhas_falha = []
for arq in trx:
    raiz = ET.parse(arq).getroot()
    c = raiz.find("t:ResultSummary/t:Counters", ns)
    total += int(c.get("total")); ok += int(c.get("passed")); falhas += int(c.get("failed"))
    for r in raiz.iterfind(".//t:UnitTestResult[@outcome='Failed']", ns):
        msg = r.find(".//t:Message", ns)
        linhas_falha.append(f"- `{r.get('testName')}` — {(msg.text or '').strip().splitlines()[0][:200] if msg is not None else ''}")
cob = glob.glob(f"{art}/testes/**/coverage.cobertura.xml", recursive=True)
cobertura = f"{float(ET.parse(cob[0]).getroot().get('line-rate')) * 100:.1f}%" if cob else "n/d"
with open(resumo, "a") as f:
    f.write(f"## {'✔ Revisão aprovada' if falhas == 0 and total > 0 else '✖ Testes falharam'}\n\n")
    f.write(f"| Testes | Aprovados | Falhas | Cobertura de linhas (informativa) | Duração |\n|---:|---:|---:|---:|---:|\n")
    f.write(f"| {total} | {ok} | {falhas} | {cobertura} | {segundos} s |\n\n")
    if linhas_falha:
        f.write("### Falhas\n" + "\n".join(linhas_falha) + "\n")
print(open(resumo).read())
PY
[ -n "${GITHUB_STEP_SUMMARY:-}" ] && cat "$RESUMO" >> "$GITHUB_STEP_SUMMARY"
[ "$codigo_testes" -eq 0 ] || falhou "testes" "veja a lista acima, $ART/testes.log e o TRX em $ART/testes/"
echo "✔ REVISÃO APROVADA"
