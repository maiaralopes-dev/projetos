# Resumo dos counters na janela com carga (300 criações/s + 300 consultas/s)

- **antes-1**: janela (40 s): CPU média 21% | threads TP máx 15 | itens TP/s médio 1297 | alocação média 40.1 MB/s | GC gen0 3.5/s | contenções de lock 6.5/s | POST ativas média 10.1
- **antes-2**: janela (40 s): CPU média 21% | threads TP máx 17 | itens TP/s médio 1369 | alocação média 43.9 MB/s | GC gen0 3.1/s | contenções de lock 4.9/s | POST ativas média 11.2
- **depois-1**: janela (40 s): CPU média 21% | threads TP máx 14 | itens TP/s médio 1351 | alocação média 43.4 MB/s | GC gen0 2.4/s | contenções de lock 6.7/s | POST ativas média 11.4
- **depois-2**: janela (40 s): CPU média 22% | threads TP máx 16 | itens TP/s médio 1296 | alocação média 55.5 MB/s | GC gen0 3.2/s | contenções de lock 5.5/s | POST ativas média 11.2
- **limitador-sem-wal-1**: janela (40 s): CPU média 24% | threads TP máx 2 | itens TP/s médio 1781 | alocação média 51.7 MB/s | GC gen0 10.1/s | contenções de lock 10.2/s | POST ativas média 0.9
- **limitador-wal-1**: janela (40 s): CPU média 24% | threads TP máx 2 | itens TP/s médio 1791 | alocação média 52.2 MB/s | GC gen0 9.7/s | contenções de lock 7.5/s | POST ativas média 0.7
- **limitador-wal-2**: janela (40 s): CPU média 24% | threads TP máx 2 | itens TP/s médio 1784 | alocação média 51.5 MB/s | GC gen0 9.7/s | contenções de lock 9.0/s | POST ativas média 0.5
- **depois-final**: janela (40 s): CPU média 23% | threads TP máx 4 | itens TP/s médio 1787 | alocação média 51.7 MB/s | GC gen0 9.2/s | contenções de lock 7.8/s | POST ativas média 0.5
