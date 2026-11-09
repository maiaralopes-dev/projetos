"""Gerador dos relatórios (python-docx). Cada relatório é descrito como uma lista de blocos."""
import os, re
from docx import Document
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_BREAK
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.abspath(os.path.join(AQUI, "..", "..", ".."))          # PortalTarefas/
LOGO = os.path.join(AQUI, "recursos", "logo-ead-unip.png")
AZUL = RGBColor(0x1F, 0x3A, 0x5F)
FONTE = "Arial"


def _borda_celula(celula, cor="000000", tam=8):
    tcPr = celula._tc.get_or_add_tcPr()
    bordas = OxmlElement("w:tcBorders")
    for lado in ("top", "left", "bottom", "right"):
        b = OxmlElement(f"w:{lado}")
        b.set(qn("w:val"), "single"); b.set(qn("w:sz"), str(tam)); b.set(qn("w:color"), cor)
        bordas.append(b)
    tcPr.append(bordas)


def _sombrear(celula, cor):
    tcPr = celula._tc.get_or_add_tcPr()
    shd = OxmlElement("w:shd"); shd.set(qn("w:val"), "clear"); shd.set(qn("w:color"), "auto"); shd.set(qn("w:fill"), cor)
    tcPr.append(shd)


def _linha_paragrafo(par, cor="5B7DB1", posicao="bottom"):
    pPr = par._p.get_or_add_pPr()
    bdr = OxmlElement("w:pBdr"); b = OxmlElement(f"w:{posicao}")
    b.set(qn("w:val"), "single"); b.set(qn("w:sz"), "6"); b.set(qn("w:space"), "1"); b.set(qn("w:color"), cor)
    bdr.append(b); pPr.append(bdr)


def _campo_pagina(par):
    for tipo, texto in (("begin", None), (None, "PAGE"), ("end", None)):
        r = par.add_run()
        if tipo:
            f = OxmlElement("w:fldChar"); f.set(qn("w:fldCharType"), tipo); r._r.append(f)
        else:
            t = OxmlElement("w:instrText"); t.set(qn("xml:space"), "preserve"); t.text = texto; r._r.append(t)


def texto_rico(par, texto, tamanho=None, cor=None):
    """Suporta **negrito**, *itálico* e `código` inline."""
    for parte in re.split(r"(\*\*[^*]+\*\*|`[^`]+`|\*[^*]+\*)", texto):
        if not parte:
            continue
        if parte.startswith("**"):
            r = par.add_run(parte[2:-2]); r.bold = True
        elif parte.startswith("`"):
            r = par.add_run(parte[1:-1]); r.font.name = "Consolas"; r.font.size = Pt((tamanho or 11) - 1.5)
            r._element.rPr.rFonts.set(qn("w:eastAsia"), "Consolas")
        elif parte.startswith("*"):
            r = par.add_run(parte[1:-1]); r.italic = True
        else:
            r = par.add_run(parte)
        if tamanho and not parte.startswith("`"):
            r.font.size = Pt(tamanho)
        if cor:
            r.font.color.rgb = cor
    return par


class Relatorio:
    def __init__(self, numero, titulo_aula, unidade, objetivo):
        self.numero, self.titulo_aula, self.unidade, self.objetivo = numero, titulo_aula, unidade, objetivo
        self.doc = Document()
        self.figura = 0
        self.tabela = 0
        self._estilos()

    # ---------- estilos e página ----------
    def _estilos(self):
        st = self.doc.styles["Normal"]
        st.font.name = FONTE; st.font.size = Pt(11)
        st.element.rPr.rFonts.set(qn("w:eastAsia"), FONTE)
        pf = st.paragraph_format; pf.space_after = Pt(4); pf.line_spacing = 1.15
        for nome, tam in (("Heading 1", 12.5), ("Heading 2", 11.5)):
            h = self.doc.styles[nome]; h.font.name = FONTE; h.font.size = Pt(tam); h.font.bold = True
            h.font.color.rgb = AZUL; h.element.rPr.rFonts.set(qn("w:eastAsia"), FONTE)
            h.paragraph_format.space_before = Pt(10 if nome == "Heading 1" else 6); h.paragraph_format.space_after = Pt(4)
            h.paragraph_format.keep_with_next = True
        s = self.doc.sections[0]
        s.page_height, s.page_width = Cm(29.7), Cm(21.0)
        s.top_margin, s.left_margin, s.bottom_margin, s.right_margin = Cm(3), Cm(3), Cm(2), Cm(2)
        s.header_distance, s.footer_distance = Cm(1.2), Cm(1.0)

    def _cabecalho_rodape(self, secao, com_numero=True):
        secao.header.is_linked_to_previous = False
        secao.footer.is_linked_to_previous = False
        h = secao.header.paragraphs[0]; h.text = ""
        _linha_paragrafo(h)
        r = h.add_run("Desenvolvimento Web com .NET · Relatório de Aulas Práticas"); r.font.size = Pt(8); r.font.color.rgb = RGBColor(0x66, 0x70, 0x85)
        f = secao.footer.paragraphs[0]; f.text = ""
        _linha_paragrafo(f, posicao="top")
        f.alignment = WD_ALIGN_PARAGRAPH.RIGHT
        if com_numero:
            r = f.add_run("Página "); r.font.size = Pt(8)
            _campo_pagina(f)
            f.add_run("    ")
        f.add_run().add_picture(LOGO, height=Cm(0.9))

    # ---------- capa (modelo da instituição) ----------
    def capa(self):
        secao = self.doc.sections[0]
        self._cabecalho_rodape(secao, com_numero=False)
        for _ in range(2):
            self.doc.add_paragraph()
        t = self.doc.add_table(rows=1, cols=1); t.alignment = WD_TABLE_ALIGNMENT.CENTER
        c = t.cell(0, 0); _borda_celula(c, tam=8); c.width = Cm(15)
        p = c.paragraphs[0]; p.alignment = WD_ALIGN_PARAGRAPH.CENTER; p.paragraph_format.space_before = Pt(18)
        p.add_run().add_picture(LOGO, width=Cm(5.5))
        for texto, negrito, tam in (("DESENVOLVIMENTO WEB COM .NET", False, 14), ("ANÁLISE E DESENVOLVIMENTO DE SISTEMAS", True, 14),
                                    ("RELATÓRIO DE AULAS PRÁTICAS", False, 14), (f"Relatório {self.numero} – {self.titulo_aula}", False, 11)):
            q = c.add_paragraph(); q.alignment = WD_ALIGN_PARAGRAPH.CENTER; q.paragraph_format.space_after = Pt(8)
            r = q.add_run(texto); r.bold = negrito; r.font.size = Pt(tam)
        for rotulo in ("Nome:", "RA:", "Polo de matrícula:", "Local da realização da Aula Prática:", "Ano da postagem: 2026"):
            q = c.add_paragraph(); q.paragraph_format.space_before = Pt(16); q.paragraph_format.left_indent = Cm(0.6)
            r = q.add_run(rotulo); r.font.size = Pt(13)
            if not rotulo.endswith("2026"):
                q.add_run(" " + "_" * max(8, 52 - int(len(rotulo) * 1.2))).font.size = Pt(13)
        c.add_paragraph().paragraph_format.space_after = Pt(18)
        nova = self.doc.add_section(WD_SECTION.NEW_PAGE)
        self._cabecalho_rodape(nova)

    # ---------- cabeçalho da atividade (tabela do modelo) ----------
    def cabecalho_atividade(self):
        t = self.doc.add_table(rows=1, cols=3); t.alignment = WD_TABLE_ALIGNMENT.CENTER
        larguras = (Cm(4.2), Cm(8.4), Cm(3.4))
        textos = ("**Instituto de Ciências Exatas e Tecnologia**",
                  f"**Disciplina:** Desenvolvimento Web com .NET\n**Título da Aula:** {self.titulo_aula}",
                  f"**Relatório {self.numero}**")
        for i, (c, w, txt) in enumerate(zip(t.rows[0].cells, larguras, textos)):
            c.width = w; _borda_celula(c, tam=6); c.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
            for j, linha in enumerate(txt.split("\n")):
                p = c.paragraphs[0] if j == 0 else c.add_paragraph()
                p.alignment = WD_ALIGN_PARAGRAPH.CENTER
                texto_rico(p, linha, tamanho=10)
        p = self.doc.add_paragraph(); p.paragraph_format.space_before = Pt(8)
        texto_rico(p, f"**{self.unidade}**")
        t = self.doc.add_table(rows=1, cols=1); c = t.cell(0, 0); _sombrear(c, "EEF2F7")
        texto_rico(c.paragraphs[0], f"**Objetivo:** {self.objetivo}", tamanho=10.5)
        self.doc.add_paragraph().paragraph_format.space_after = Pt(0)

    # ---------- blocos ----------
    def h1(self, texto): self.doc.add_heading(texto, level=1)
    def h2(self, texto): self.doc.add_heading(texto, level=2)

    def p(self, texto, tamanho=None):
        par = self.doc.add_paragraph()
        # Justificar com muitos trechos de código cria lacunas grandes; nesses casos alinha à esquerda.
        par.alignment = WD_ALIGN_PARAGRAPH.LEFT if texto.count("`") >= 8 else WD_ALIGN_PARAGRAPH.JUSTIFY
        return texto_rico(par, texto, tamanho)

    def itens(self, lista, numerada=False, tamanho=None):
        for item in lista:
            par = self.doc.add_paragraph(style="List Number" if numerada else "List Bullet")
            par.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY; par.paragraph_format.space_after = Pt(2)
            texto_rico(par, item, tamanho)

    def tabela_dados(self, cabecalho, linhas, larguras=None, titulo=None, tamanho=9):
        if titulo:
            self.tabela += 1
            cap = self.doc.add_paragraph(); cap.paragraph_format.keep_with_next = True
            texto_rico(cap, f"**Tabela {self.tabela}** – {titulo}", tamanho=9.5)
        t = self.doc.add_table(rows=1, cols=len(cabecalho)); t.style = "Table Grid"; t.alignment = WD_TABLE_ALIGNMENT.CENTER
        for i, txt in enumerate(cabecalho):
            c = t.rows[0].cells[i]; _sombrear(c, "DCE4EF"); texto_rico(c.paragraphs[0], f"**{txt}**", tamanho)
        for linha in linhas:
            cells = t.add_row().cells
            for i, txt in enumerate(linha):
                for j, parte in enumerate(str(txt).split("\n")):
                    par = cells[i].paragraphs[0] if j == 0 else cells[i].add_paragraph()
                    par.paragraph_format.space_after = Pt(0)
                    texto_rico(par, parte, tamanho)
        if larguras:
            for row in t.rows:
                for i, w in enumerate(larguras):
                    row.cells[i].width = Cm(w)
        self.doc.add_paragraph().paragraph_format.space_after = Pt(0)

    def codigo(self, texto, titulo=None, tamanho=7.5):
        if titulo:
            cap = self.doc.add_paragraph(); cap.paragraph_format.keep_with_next = True
            texto_rico(cap, titulo, tamanho=9)
        t = self.doc.add_table(rows=1, cols=1); c = t.cell(0, 0); _sombrear(c, "F5F7FA"); _borda_celula(c, "C9D2DE", 4)
        par = c.paragraphs[0]; par.paragraph_format.line_spacing = 1.0; par.paragraph_format.space_after = Pt(0)
        linhas = texto.rstrip("\n").split("\n")
        for i, linha in enumerate(linhas):
            r = par.add_run(linha.replace("\t", "    ")); r.font.name = "Consolas"; r.font.size = Pt(tamanho)
            r._element.rPr.rFonts.set(qn("w:eastAsia"), "Consolas")
            if i < len(linhas) - 1:
                r.add_break()
        self.doc.add_paragraph().paragraph_format.space_after = Pt(0)

    def arquivo(self, caminho_relativo, titulo=None, tamanho=7, de=None, ate=None):
        caminho = os.path.join(RAIZ, caminho_relativo)
        texto = open(caminho, encoding="utf-8-sig").read()
        if de or ate:
            linhas = texto.split("\n"); texto = "\n".join(linhas[(de or 1) - 1:ate])
        self.codigo(texto, titulo or f"**Arquivo:** `{caminho_relativo}`", tamanho)

    def figura_img(self, caminho_relativo, legenda, largura_cm=14, rotulo=None):
        if rotulo is None:
            self.figura += 1
        par = self.doc.add_paragraph(); par.alignment = WD_ALIGN_PARAGRAPH.CENTER; par.paragraph_format.keep_with_next = True
        par.add_run().add_picture(os.path.join(RAIZ, caminho_relativo), width=Cm(largura_cm))
        cap = self.doc.add_paragraph(); cap.alignment = WD_ALIGN_PARAGRAPH.CENTER
        texto_rico(cap, f"**Figura {rotulo or self.figura}** – {legenda}", tamanho=9)
        return self.figura

    def figuras_apendice(self, pasta, itens, largura_cm=11):
        """itens: (arquivo, 'B1', legenda)"""
        for arq, rot, leg in itens:
            self.figura_img(f"{pasta}/{arq}", leg, largura_cm, rotulo=rot)

    def nova_pagina(self):
        self.doc.add_paragraph().add_run().add_break(WD_BREAK.PAGE)

    def referencias(self, lista):
        self.h1("Referências")
        for ref in lista:
            par = self.doc.add_paragraph(); par.paragraph_format.space_after = Pt(6); par.alignment = WD_ALIGN_PARAGRAPH.LEFT
            texto_rico(par, ref, tamanho=10)

    def salvar(self, nome):
        destino = os.path.join(RAIZ, "docs", "relatorios", nome)
        self.doc.save(destino)
        return destino


AMBIENTE = [
    ["SDK .NET / runtime", "SDK 10.0.114 (CLI, Linux) · runtime ASP.NET Core 10.0.14 · C# 14"],
    ["IDE", "Visual Studio 2026 18.0 indicado no roteiro; o projeto foi produzido e verificado pela CLI `dotnet` (VS opcional)"],
    ["Pacotes principais", "EF Core 10.0.14 (SQL Server) · Dapper 2.1.89 · Microsoft.AspNetCore.OpenApi 10.0.14 · Scalar 2.19.0"],
    ["Testes", "xUnit 2.9.3 · Microsoft.AspNetCore.Mvc.Testing 10.0.14 · Vitest 5.1 · Testing Library 16"],
    ["Front-end", "Node.js 22.23.4 · npm 10.9.9 · Vite 8.3 · React 19.3 · React Router 8.5 · TypeScript 7.0 · NSwag 14.7.1"],
    ["Banco e contêineres", "SQL Server 2022 (imagem `mssql/server:2022-latest`) · Docker 29.9.2 · Docker Compose 5.6.1"],
]

REPO = "https://github.com/maiaralopes-dev/projetos (branch `ccr-836bb896-v5ejij`, pasta `PortalTarefas/`)"


def acesso(titulo, url, ano="2025", autor="MICROSOFT", site="Microsoft Learn"):
    return f"{autor}. **{titulo}**. {site}, {ano}. Disponível em: {url}. Acesso em: 9 nov. 2026."


def git_show(commit, caminho_relativo):
    """Conteúdo de um arquivo do projeto no commit da atividade (código exatamente como entregue naquela etapa)."""
    import subprocess
    return subprocess.run(["git", "show", f"{commit}:PortalTarefas/{caminho_relativo}"], cwd=RAIZ,
                           capture_output=True, text=True, check=True).stdout


def trecho(caminho_relativo, inicio=None, fim=None, contem=None, max_linhas=None, cortar=170):
    """Trecho de um arquivo de evidência: por intervalo de linhas ou entre marcadores."""
    linhas = open(os.path.join(RAIZ, caminho_relativo), encoding="utf-8").read().split("\n")
    if contem:
        linhas = [l for l in linhas if any(c in l for c in contem)]
    if inicio is not None:
        linhas = linhas[inicio - 1:fim]
    if max_linhas:
        linhas = linhas[:max_linhas]
    return "\n".join(l if len(l) <= cortar else l[:cortar] + " …" for l in linhas)


def bloco_entre(caminho_relativo, titulo_inicio, titulo_fim=None, max_linhas=40):
    """Trecho de requisicoes.txt entre dois títulos '### ...'."""
    texto = open(os.path.join(RAIZ, caminho_relativo), encoding="utf-8").read()
    i = texto.index(titulo_inicio)
    j = texto.index(titulo_fim, i + 1) if titulo_fim else len(texto)
    return "\n".join(l if len(l) <= 175 else l[:175] + " …" for l in texto[i:j].rstrip().split("\n")[:max_linhas])


Relatorio.arquivo_git = lambda self, commit, caminho, tamanho=7, titulo=None: self.codigo(
    git_show(commit, caminho), titulo or f"**Arquivo:** `{caminho}` (commit `{commit}`)", tamanho)
