# Implementação — usabilidade do mapa e importação OSM

## Fases

### 1. Desenho e posição inicial

- Exibir e numerar vértices.
- Preservar pontos ao fechar a área.
- Abrir o mapa vazio em Teixeira de Freitas.

### 2. Navegação pelos cadastros

- Tornar cards focáveis por mouse e teclado.
- Destacar o território selecionado.
- Impedir que ações internas acionem o foco por engano.

### 3. Importação OSM

- Ler XML com segurança e limite de tamanho.
- Montar polígonos de caminhos e relações.
- Listar candidatos e aplicar a escolha ao editor.
- Preencher origem e referência externa do OSM.

### 4. Validação e publicação

- Testar conversão, desenho e seleção.
- Compilar, verificar dependências e publicar.

## Critério de conclusão

O usuário consegue desenhar vendo cada vértice, importar um limite `.osm`, escolher a área correta, clicar em qualquer card para focar o mapa e iniciar uma sessão sobre Teixeira de Freitas.

