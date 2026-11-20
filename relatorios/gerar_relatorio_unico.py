"""Gera UM documento com os relatórios 1 a 8 (Markdown, DOCX e PDF).

Uso (a partir da pasta relatorios/):
    python3 gerar_relatorio_unico.py
Requer pandoc, python-docx e, para o PDF/sumário, LibreOffice.
Fonte: Relatorio1.md … Relatorio8.md (edite-os e gere de novo).
"""
import re
import subprocess
import sys
from pathlib import Path

from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_LINE_SPACING
from docx.shared import Cm, Pt, RGBColor

AQUI = Path(__file__).resolve().parent
SAIDA = "Relatorio_Completo_1a8"
QUEBRA = '\n```{=openxml}\n<w:p><w:r><w:br w:type="page"/></w:r></w:p>\n```\n'
SUMARIO = """
```{=openxml}
<w:sdt><w:sdtPr><w:docPartObj><w:docPartGallery w:val="Table of Contents"/><w:docPartUnique/></w:docPartObj></w:sdtPr><w:sdtContent>
<w:p><w:pPr><w:pStyle w:val="TOCHeading"/></w:pPr><w:r><w:t>SUMÁRIO</w:t></w:r></w:p>
<w:p><w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r><w:r><w:instrText xml:space="preserve"> TOC \\o "1-1" \\h \\z \\u </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>Atualize este campo (Word: clique com o botão direito → Atualizar campo) para montar o sumário.</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
</w:sdtContent></w:sdt>
```
"""

CAPA = """::: {custom-style="Capa Titulo"}
PROGRAMAÇÃO APLICADA EM .NET
:::

::: {custom-style="Capa Titulo"}
ANÁLISE E DESENVOLVIMENTO DE SISTEMAS
:::

::: {custom-style="Capa Titulo"}
RELATÓRIO DE AULAS PRÁTICAS
:::

::: {custom-style="Capa Subtitulo"}
Relatórios 1 a 8 — Hub de Processamento de Pedidos
:::

::: {custom-style="Capa Campo"}
Nome: ___________________________________________
:::

::: {custom-style="Capa Campo"}
RA: ______________________________________________
:::

::: {custom-style="Capa Campo"}
Polo de matrícula: __________________________________
:::

::: {custom-style="Capa Campo"}
Local da realização da Aula Prática: _____________________
:::

::: {custom-style="Capa Campo"}
Ano da postagem: 2026
:::
"""

APRESENTACAO = """# Apresentação

Este documento reúne os oito relatórios de aulas práticas da disciplina **Programação Aplicada em .NET**.
As atividades são cumulativas: todas evoluem a mesma aplicação, o **Hub de Processamento de Pedidos**,
uma API em .NET 10 / C# 14 / ASP.NET Core 10 / EF Core 10. Cada atividade parte da versão executável
da anterior.

| Atividade | Eixo |
|---|---|
| 1 | Pattern matching, atributos, reflexão controlada, records e DTOs imutáveis |
| 2 | Span&lt;T&gt;, Memory&lt;T&gt;, pooling e caminhos críticos limitados por E/S e medidos |
| 3 | Consultas previsíveis, projeções, tracking, consultas compiladas e JSON |
| 4 | Channels, SemaphoreSlim, coleções concorrentes e backpressure |
| 5 | BenchmarkDotNet, counters, traces, p95/p99 e testes de carga |
| 6 | Modularização, IoC, fronteiras e versionamento compatível de APIs |
| 7 | SignalR, reconexão, streaming, cache de resposta e ETags |
| 8 | WebApplicationFactory, bancos efêmeros, analisadores e revisão automatizada |

**Repositório e reprodução.** O código, os testes, os scripts e as evidências de execução estão na pasta
`HubPedidos/` do repositório entregue. As evidências citadas em cada capítulo ficam em
`HubPedidos/docs/evidencias/atividadeN/`. Para reproduzir: `dotnet test` (suíte completa) ou `./revisao.sh`
(revisão completa, igual à do CI).

**Ambiente.** Cada capítulo registra as versões que influenciam o resultado. As medições de desempenho
(atividades 2 a 5 e 7) foram feitas numa máquina virtual Linux com 4 núcleos e devem ser repetidas no
ambiente do(a) aluno(a) antes da entrega final, trocando os números das tabelas.
"""


def capitulo(numero: int) -> str:
    texto = (AQUI / f"Relatorio{numero}.md").read_text(encoding="utf-8")
    inicio = texto.index("## Relatório")  # descarta a capa repetida de cada arquivo
    corpo = texto[inicio:]
    # "## Relatório N — …" vira capítulo (nível 1); o restante mantém a hierarquia.
    corpo = re.sub(r"^## (Relatório \d+ —.*)$", r"# \1", corpo, count=1, flags=re.M)
    # Imagem seguida da linha "*Figura N: …*": a linha vira a legenda (sem legenda duplicada).
    corpo = re.sub(r"!\[[^\]]*\]\(([^)]+)\)\n\n\*(Figura [^\n]+?)\*\n", r"![\2](\1)\n", corpo)
    return corpo.strip() + "\n"


def montar_markdown() -> Path:
    partes = [CAPA, QUEBRA, SUMARIO, QUEBRA, APRESENTACAO]
    for n in range(1, 9):
        partes += [QUEBRA, capitulo(n)]
    md = AQUI / f"{SAIDA}.md"
    md.write_text("\n".join(partes), encoding="utf-8")
    return md


def criar_modelo_estilos() -> Path:
    """Modelo de estilos no padrão de trabalho acadêmico (ABNT): Arial 12, 1,5, margens 3/3/2/2 cm."""
    modelo = AQUI / ".modelo-estilos.docx"
    subprocess.run(["pandoc", "-o", str(modelo), "--print-default-data-file", "reference.docx"], check=True)
    doc = Document(str(modelo))
    for secao in doc.sections:
        secao.page_height, secao.page_width = Cm(29.7), Cm(21.0)
        secao.top_margin, secao.left_margin = Cm(3), Cm(3)
        secao.bottom_margin, secao.right_margin = Cm(2), Cm(2)

    def ajustar(nome, tamanho=12, negrito=None, alinhamento=None, espaco=1.5, antes=0, depois=6, cor=True, fonte="Arial"):
        # Busca percorrendo a lista: o python-docx traduz "Heading 1" ↔ "heading 1" no índice
        # por nome, e o modelo do pandoc usa a outra grafia.
        estilo = next((s for s in doc.styles if s.name.lower() == nome.lower()), None) or doc.styles.add_style(nome, 1)
        estilo.font.name = fonte
        estilo.font.size = Pt(tamanho)
        if negrito is not None:
            estilo.font.bold = negrito
        if cor:
            estilo.font.color.rgb = RGBColor(0, 0, 0)
        fmt = estilo.paragraph_format
        fmt.line_spacing_rule = WD_LINE_SPACING.MULTIPLE
        fmt.line_spacing = espaco
        fmt.space_before, fmt.space_after = Pt(antes), Pt(depois)
        if alinhamento is not None:
            fmt.alignment = alinhamento
        return estilo

    # Número de página no rodapé (campo PAGE).
    from docx.oxml import OxmlElement
    from docx.oxml.ns import qn
    for secao in doc.sections:
        par = secao.footer.paragraphs[0] if secao.footer.paragraphs else secao.footer.add_paragraph()
        par.alignment = WD_ALIGN_PARAGRAPH.RIGHT
        for tipo, texto in (("begin", None), (None, " PAGE "), ("end", None)):
            run = par.add_run()
            if tipo:
                fld = OxmlElement("w:fldChar")
                fld.set(qn("w:fldCharType"), tipo)
                run._r.append(fld)
            else:
                instr = OxmlElement("w:instrText")
                instr.set(qn("xml:space"), "preserve")
                instr.text = texto
                run._r.append(instr)

    ajustar("Normal")
    nomes = [s.name.lower() for s in doc.styles]
    for nome in ("Body Text", "First Paragraph", "Compact"):
        if nome.lower() in nomes:
            ajustar(nome, alinhamento=WD_ALIGN_PARAGRAPH.JUSTIFY if nome != "Compact" else None)
    ajustar("Heading 1", 14, True, antes=0, depois=12)
    ajustar("Heading 2", 12, True, antes=12, depois=6)
    ajustar("Heading 3", 12, True, antes=6, depois=6)
    if "source code" in nomes:
        ajustar("Source Code", 9, espaco=1.0, depois=0, cor=False, fonte="Consolas")
    ajustar("Capa Titulo", 14, True, WD_ALIGN_PARAGRAPH.CENTER, antes=0, depois=6)
    ajustar("Capa Subtitulo", 12, False, WD_ALIGN_PARAGRAPH.CENTER, antes=36, depois=120)
    ajustar("Capa Campo", 12, False, WD_ALIGN_PARAGRAPH.LEFT, espaco=1.5, depois=18)
    if "toc heading" in nomes:
        ajustar("TOC Heading", 14, True, WD_ALIGN_PARAGRAPH.CENTER)
    doc.save(str(modelo))
    return modelo


def ajustar_tabelas(docx: Path) -> None:
    """Tabelas legíveis: fonte 9,5 pt, espaçamento simples e largura de coluna proporcional ao conteúdo."""
    from docx.oxml import OxmlElement
    from docx.oxml.ns import qn

    doc = Document(str(docx))
    largura_util = Cm(16)  # A4 (21 cm) − margens 3 + 2 cm
    for tabela in doc.tables:
        colunas = len(tabela.columns)
        pesos = []
        for c in range(colunas):
            textos = [tabela.rows[r].cells[c].text for r in range(len(tabela.rows)) if c < len(tabela.rows[r].cells)]
            maior_palavra = max((len(p) for t in textos for p in t.split()), default=4)
            media = sum(len(t) for t in textos) / max(1, len(textos))
            pesos.append(max(maior_palavra * 1.45, min(media, 60), 4))
        total = sum(pesos)
        larguras = [int(largura_util * p / total) for p in pesos]
        layout = OxmlElement("w:tblLayout")
        layout.set(qn("w:type"), "fixed")
        tabela._tbl.tblPr.append(layout)
        grade = tabela._tbl.tblGrid
        for c, col in enumerate(grade.findall(qn("w:gridCol"))):
            if c < colunas:
                col.set(qn("w:w"), str(int(larguras[c] / 635)))  # EMU → twips
        for linha in tabela.rows:
            for c, celula in enumerate(linha.cells):
                if c < colunas:
                    celula.width = larguras[c]
                for par in celula.paragraphs:
                    par.paragraph_format.line_spacing = 1.0
                    par.paragraph_format.space_before = Pt(1)
                    par.paragraph_format.space_after = Pt(1)
                    for run in par.runs:
                        run.font.size = Pt(9.5)
        # Respiro entre a tabela e o parágrafo seguinte.
        seguinte = tabela._tbl.getnext()
        if seguinte is not None and seguinte.tag == qn("w:p"):
            from docx.text.paragraph import Paragraph
            Paragraph(seguinte, tabela._parent).paragraph_format.space_before = Pt(10)
    doc.save(str(docx))


def atualizar_sumario_e_exportar(docx: Path) -> None:
    """Abre no LibreOffice (sem interface), atualiza o sumário e salva DOCX + PDF."""
    import time

    import uno
    from com.sun.star.beans import PropertyValue

    def prop(nome, valor):
        p = PropertyValue()
        p.Name, p.Value = nome, valor
        return p

    canal = "pipe,name=hubpedidos-relatorio"
    processo = subprocess.Popen(["soffice", "--headless", "--invisible", "--norestore", f"--accept={canal};urp;"])
    try:
        resolvedor = uno.getComponentContext().ServiceManager.createInstanceWithContext("com.sun.star.bridge.UnoUrlResolver", uno.getComponentContext())
        for _ in range(60):
            try:
                contexto = resolvedor.resolve(f"uno:{canal};urp;StarOffice.ComponentContext")
                break
            except Exception:
                time.sleep(0.5)
        else:
            raise RuntimeError("LibreOffice não respondeu")
        desktop = contexto.ServiceManager.createInstanceWithContext("com.sun.star.frame.Desktop", contexto)
        doc = desktop.loadComponentFromURL(docx.as_uri(), "_blank", 0, (prop("Hidden", True),))
        for _ in range(2):  # 2 passadas: o próprio sumário muda a paginação
            indices = doc.getDocumentIndexes()
            for i in range(indices.getCount()):
                indices.getByIndex(i).update()
            doc.refresh()
        doc.storeToURL(docx.as_uri(), (prop("FilterName", "MS Word 2007 XML"),))
        doc.storeToURL(docx.with_suffix(".pdf").as_uri(), (prop("FilterName", "writer_pdf_Export"),))
        doc.close(True)
    finally:
        processo.terminate()
        processo.wait(timeout=30)


def main() -> int:
    md = montar_markdown()
    modelo = criar_modelo_estilos()
    docx = AQUI / f"{SAIDA}.docx"
    subprocess.run(["pandoc", str(md.name), "-f", "markdown", "-o", str(docx.name), "--reference-doc", str(modelo.name),
                    "--resource-path", str(AQUI)], cwd=AQUI, check=True)
    ajustar_tabelas(docx)
    atualizar_sumario_e_exportar(docx)
    print(f"gerado: {md.name}, {docx.name}, {docx.with_suffix('.pdf').name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
