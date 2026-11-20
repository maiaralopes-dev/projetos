"""Relatório único (atividades 1 a 8) a partir dos geradores r1..r8.

Um único documento recebe todos os blocos. Cada atividade vira um capítulo com o cabeçalho do modelo;
tabelas e figuras são numeradas por atividade (ex.: Tabela 3.2, Figura 6.B1); as referências são
unificadas no fim e os apêndices de todas as atividades vão para o final do documento.
Uso: python3 docs/relatorios/gerador/consolidado.py
"""
import importlib, os, re, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import base
from base import Relatorio, RAIZ, texto_rico
from docx.shared import Pt

TITULOS = {}


class Consolidado(Relatorio):
    def __init__(self):
        super().__init__("1 a 8", "Portal de Tarefas – atividades práticas 1 a 8", "", "")
        h3 = self.doc.styles["Heading 3"]
        h3.font.name = base.FONTE; h3.font.size = Pt(11); h3.font.bold = True; h3.font.color.rgb = base.AZUL
        self.atividade = 0
        self.apendice = False
        self.adiados = []          # blocos de apêndice, reproduzidos no fim
        self.refs = []

    # -- prefixa referências cruzadas "Tab. 3", "Fig. B2", "Figura 1" com o número da atividade --
    def _ref(self, texto):
        if not isinstance(texto, str) or not self.atividade:
            return texto
        n = self.atividade
        texto = re.sub(r"\b(Tab\.|Tabela|Fig\.|Figuras?|Figs?\.)\s+(B?\d+)", lambda m: f"{m.group(1)} {n}.{m.group(2)}", texto)
        if "Fig" in texto:   # demais itens de listas como "Fig. 3.1, B1–B3" ou "Figuras B1 a B9"
            texto = re.sub(r"(?<![\w.])B(\d+)\b", lambda m: f"{n}.B{m.group(1)}", texto)
        return texto

    def iniciar(self, numero, titulo, unidade, objetivo):
        if self.atividade:
            self.nova_pagina()
        self.atividade, self.apendice = numero, False
        self.figura = self.tabela = 0
        self.numero, self.titulo_aula, self.unidade, self.objetivo = numero, titulo, unidade, objetivo
        self.doc.add_heading(f"Atividade {numero} – {titulo}", level=1)
        Relatorio.cabecalho_atividade(self)

    # chamadas feitas pelos geradores individuais
    def capa(self): pass
    def cabecalho_atividade(self): pass
    def salvar(self, nome): return None

    def _adiar(self, nome, *a, **k):
        if self.apendice:
            self.adiados.append((self.atividade, nome, a, k)); return True
        return False

    def h1(self, texto):
        if texto.startswith("Apêndice"):
            self.apendice = True
            self.adiados.append((self.atividade, "_titulo_apendice", (texto,), {})); return
        if texto == "Referências":
            return
        self.doc.add_heading(texto, level=2)

    def h2(self, texto):
        if not self._adiar("h2", texto): self.doc.add_heading(texto, level=3)

    def p(self, texto, tamanho=None):
        if not self._adiar("p", texto, tamanho): return Relatorio.p(self, self._ref(texto), tamanho)

    def itens(self, lista, numerada=False, tamanho=None):
        if not self._adiar("itens", lista, numerada, tamanho):
            Relatorio.itens(self, [self._ref(i) for i in lista], numerada, tamanho)

    def tabela_dados(self, cabecalho, linhas, larguras=None, titulo=None, tamanho=9):
        if self._adiar("tabela_dados", cabecalho, linhas, larguras, titulo, tamanho): return
        linhas = [[self._ref(c) for c in l] for l in linhas]
        if titulo:
            self.tabela += 1
            cap = self.doc.add_paragraph(); cap.paragraph_format.keep_with_next = True
            texto_rico(cap, f"**Tabela {self.atividade}.{self.tabela}** – {titulo}", tamanho=9.5)
        Relatorio.tabela_dados(self, cabecalho, linhas, larguras, None, tamanho)

    def codigo(self, texto, titulo=None, tamanho=7.5):
        if not self._adiar("codigo", texto, titulo, tamanho): Relatorio.codigo(self, texto, titulo, tamanho)

    def figura_img(self, caminho, legenda, largura_cm=14, rotulo=None):
        if self._adiar("figura_img", caminho, legenda, largura_cm, rotulo): return
        if rotulo is None:
            self.figura += 1; rotulo = f"{self.atividade}.{self.figura}"
        elif not str(rotulo).startswith(f"{self.atividade}."):
            rotulo = f"{self.atividade}.{rotulo}"
        Relatorio.figura_img(self, caminho, legenda, largura_cm, rotulo=rotulo)

    def figuras_apendice(self, pasta, itens, largura_cm=11):
        for arq, rot, leg in itens:
            self.figura_img(f"{pasta}/{arq}", leg, largura_cm, rotulo=rot)

    def nova_pagina(self):
        if not self._adiar("nova_pagina"): Relatorio.nova_pagina(self)

    def referencias(self, lista):
        for ref in lista:
            if ref not in self.refs: self.refs.append(ref)

    # -- partes exclusivas do documento único --
    def indice(self):
        self.doc.add_heading("Sumário", level=1)
        for n in range(1, 9):
            t, u = TITULOS[n]
            par = self.doc.add_paragraph(); par.paragraph_format.space_after = Pt(3)
            texto_rico(par, f"**Atividade {n}** – {t}  ·  *{u}*", tamanho=10.5)
        for extra in ("Referências (unificadas)", "Apêndices A (código-fonte) e B (evidências) de cada atividade"):
            par = self.doc.add_paragraph(); texto_rico(par, f"**{extra}**", tamanho=10.5)
        par = self.doc.add_paragraph(); par.paragraph_format.space_before = Pt(10)
        texto_rico(par, "Repositório: " + base.REPO + ". Tabelas e figuras são numeradas por atividade "
                   "(ex.: Tabela 3.2 = 2ª tabela da Atividade 3; Figura 6.B1 = figura B1 do apêndice da Atividade 6).", tamanho=10)

    def finalizar(self):
        Relatorio.nova_pagina(self)
        self.atividade = 0
        self.doc.add_heading("Referências", level=1)
        for ref in sorted(self.refs, key=lambda r: re.sub(r"[*]", "", r).upper()):
            par = self.doc.add_paragraph(); par.paragraph_format.space_after = Pt(6)
            texto_rico(par, ref, tamanho=10)
        self.apendice = False
        for n, nome, a, k in self.adiados:
            self.atividade = n
            if nome == "_titulo_apendice":
                Relatorio.nova_pagina(self)
                self.doc.add_heading(f"Atividade {n} – {a[0]}", level=1)
            else:
                getattr(self, nome)(*a, **k)


def gerar():
    doc = Consolidado()
    # Cada gerador rN cria "Relatorio(...)"; aqui ele recebe o documento único já posicionado na atividade.
    def fabrica(n):
        def nova(numero, titulo, unidade, objetivo):
            TITULOS[numero] = (titulo, unidade)
            doc.iniciar(numero, titulo, unidade, objetivo)
            return doc
        return nova

    modulos = [importlib.import_module(f"r{n}") for n in range(1, 9)]
    # 1ª passada só para colher os títulos do sumário
    for m in modulos:
        m.Relatorio = (lambda numero, titulo, unidade, objetivo: TITULOS.__setitem__(numero, (titulo, unidade)) or _Nulo())
        m.gerar()

    doc.numero, doc.titulo_aula = "1 a 8", "Portal de Tarefas – atividades práticas 1 a 8"
    Relatorio.capa(doc)
    doc.indice()
    for n, m in enumerate(modulos, 1):
        m.Relatorio = fabrica(n)
        m.gerar()
    doc.finalizar()
    destino = os.path.join(RAIZ, "docs", "relatorios", "Relatorio-Unico-Atividades-1-a-8.docx")
    doc.doc.save(destino)
    return destino


class _Nulo:
    """Objeto que aceita qualquer chamada (usado só para ler os títulos)."""
    def __getattr__(self, _): return lambda *a, **k: None


if __name__ == "__main__":
    docx = gerar()
    subprocess.run(["soffice", "--headless", "--convert-to", "pdf", "--outdir", os.path.dirname(docx), docx], check=True, capture_output=True)
    print("gerado:", os.path.relpath(docx, RAIZ), "+ PDF")
