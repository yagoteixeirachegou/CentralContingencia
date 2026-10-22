import '@testing-library/jest-dom/vitest'


import { configure } from "@testing-library/react"
// Telas carregam por import dinâmico; sob carga (testes em paralelo) o padrão de 1 s é curto e gera falhas intermitentes.
configure({ asyncUtilTimeout: 5000 })
