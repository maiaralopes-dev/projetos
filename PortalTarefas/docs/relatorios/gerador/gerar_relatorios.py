"""Gera os relatórios .docx (editáveis) e converte para PDF com o LibreOffice.
Uso: python3 docs/relatorios/gerador/gerar_relatorios.py [1 2 ...]"""
import importlib, os, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from base import RAIZ

numeros = [int(n) for n in sys.argv[1:]] or list(range(1, 9))
saida = os.path.join(RAIZ, "docs", "relatorios")
for n in numeros:
    docx = importlib.import_module(f"r{n}").gerar()
    subprocess.run(["soffice", "--headless", "--convert-to", "pdf", "--outdir", saida, docx],
                   check=True, capture_output=True)
    print("gerado:", os.path.relpath(docx, RAIZ), "+ PDF")
