import type { AdsPowerProfile } from '../dashboard/api'

// =============================================================================
// Tipos
// =============================================================================

/** Modo de execução: só verifica impedimentos, ou faz a sessão completa. */
export type WarmupMode = 'verification' | 'session'

/** Dados mínimos de um perfil — shape que o prompt espera. */
export type WarmupProfileInput = {
  name: string
  groupName: string
  /** ID interno do AdsPower (user_id). Opcional na verificação, usado no fechamento da sessão. */
  adsPowerId?: string
}

/** Parâmetros configuráveis do prompt (vindos do formulário / defaults do grupo). */
export interface WarmupOptions {
  /** Modo do prompt. Default: 'session'. */
  mode?: WarmupMode
  /** Duração total da sessão, ex.: "5m30s". Default: "5m30s". */
  duration?: string
  /** Quantidade alvo de curtidas distribuídas. Default: 5. */
  likes?: number
  /** URL base da API local do AdsPower. Default: "http://127.0.0.1:50325". */
  apiBase?: string
}

const DEFAULTS: Required<Omit<WarmupOptions, 'mode'>> = {
  duration: '5m30s',
  likes: 5,
  apiBase: 'http://127.0.0.1:50325',
}

// =============================================================================
// Adapter — AdsPowerProfile → WarmupProfileInput
// =============================================================================

/**
 * Converte um AdsPowerProfile do dashboard no shape que o prompt espera.
 * `groupName` não vem no perfil, então precisa ser passado pelo caller
 * (normalmente `chosenGroup.groupName`).
 */
export function toWarmupInput(
  profile: AdsPowerProfile,
  groupName: string,
): WarmupProfileInput {
  return {
    name: profile.name,
    groupName,
    adsPowerId: profile.profileId,
  }
}

// =============================================================================
// Blocos reutilizáveis (evitam repetição entre modos)
// =============================================================================

const CATEGORIES =
  'Ok | Captcha | VerificationRequested | LoginRequired | IdentityConfirmation | Checkpoint | AccountRestriction | NavigationError'

function buildProfileList(profiles: readonly WarmupProfileInput[]): string {
  return profiles
    .map((profile, index) => {
      const id = profile.adsPowerId ? ` [id ${profile.adsPowerId}]` : ''
      return `${index + 1}. ${profile.name} (grupo ${profile.groupName})${id}`
    })
    .join('\n')
}

function buildSafetyBlock(): string {
  return [
    `Se a categoria for diferente de Ok: não tente contornar, confirmar nem repetir.`,
    `NÃO feche o navegador nem chame stop. Deixe a página aberta como está, informe a categoria e PARE.`,
  ].join('\n')
}

function buildClosingBlock(apiBase: string): string {
  return [
    `Se for Ok: feche o perfil pela mesma ferramenta do AdsPower usada para abri-lo`,
    `(ou via API: GET ${apiBase}/api/v1/browser/stop?user_id=<ID>, com header Authorization: Bearer <token>),`,
    `confirme "code": 0 e siga para o próximo.`,
  ].join('\n')
}

function buildResponseBlock(extra?: string): string {
  const base = `Ao final, responda com uma linha por perfil: perfil, grupo, categoria, data/hora UTC e se foi fechado.`
  return extra ? `${base}\n${extra}` : base
}

// =============================================================================
// Prompt — Verificação (somente leitura)
// =============================================================================

export function buildVerificationPrompt(
  profiles: readonly WarmupProfileInput[],
  options: WarmupOptions = {},
): string {
  const { apiBase } = { ...DEFAULTS, ...options }

  return [
    `Verifique, um perfil por vez e na ordem abaixo, os perfis do AdsPower:`,
    buildProfileList(profiles),
    ``,
    `Para cada perfil: abra o perfil (o Facebook já está autenticado) e carregue UMA página: https://www.facebook.com/ . Aguarde terminar de carregar.`,
    `Apenas observe o estado da página. Não role, não clique, não abra Reels, não curta, não comente, não envie mensagens nem solicitações e não altere nada no perfil.`,
    ``,
    `Classifique o resultado em UMA categoria: ${CATEGORIES}.`,
    ``,
    buildSafetyBlock(),
    buildClosingBlock(apiBase),
    ``,
    buildResponseBlock(),
  ].join('\n')
}

// =============================================================================
// Prompt — Sessão completa (aquecimento)
// =============================================================================

export function buildSessionPrompt(
  profiles: readonly WarmupProfileInput[],
  options: WarmupOptions = {},
): string {
  const { duration, likes, apiBase } = { ...DEFAULTS, ...options }

  return [
    `Execute uma sessão de aquecimento, um perfil por vez e na ordem abaixo, nos perfis do AdsPower:`,
    buildProfileList(profiles),
    ``,
    `Para cada perfil: abra o perfil (o Facebook já está autenticado) e carregue UMA página: https://www.facebook.com/ . Aguarde terminar de carregar.`,
    ``,
    `Classifique o estado inicial em UMA categoria: ${CATEGORIES}.`,
    ``,
    buildSafetyBlock(),
    `Somente se a categoria inicial for Ok, execute a sessão abaixo.`,
    ``,
    `Sessão (~${duration}):`,
    `- Comece navegando normalmente pelo Feed do Facebook.`,
    `- Role a página e visualize alguns dos conteúdos encontrados.`,
    `- Assista a alguns vídeos que aparecerem durante a navegação.`,
    `- Durante toda a sessão, curta aproximadamente ${likes} publicações ou vídeos,`,
    `  distribuindo essas interações ao longo do tempo. Não é necessário curtir todos os conteúdos visualizados.`,
    `- Se a aba Reels estiver disponível, acesse-a durante a sessão.`,
    `- Na aba Reels, assista a alguns vídeos e alterne entre eles.`,
    `- As ~${likes} curtidas podem ser distribuídas entre publicações do Feed e Reels.`,
    `- NÃO faça comentários, compartilhamentos, solicitações de amizade, envio de mensagens`,
    `  ou alterações no perfil durante este teste.`,
    `- NÃO execute várias interações simultaneamente ou em sequência imediata.`,
    `- Quando o tempo total da sessão atingir aproximadamente ${duration}, encerre a atividade.`,
    ``,
    `Durante toda a execução, interrompa imediatamente se o Facebook apresentar:`,
    `CAPTCHA; solicitação de verificação; pedido de login ou nova autenticação;`,
    `confirmação de identidade; checkpoint; aviso ou restrição relacionada à conta;`,
    `erro que impeça a navegação normal.`,
    ``,
    buildClosingBlock(apiBase),
    ``,
    buildResponseBlock(
      `Registre também via POST /api/v1/warmup/checks (Admin/Manager, cookie + CSRF):`,
    ),
    `{ "adsPowerProfileId": "<ID>", "profileName": "<nome>", "adsPowerGroupId": "<grupo>", "outcome": "<categoria>" }`,
    ``,
    `Se tudo ocorreu sem impedimentos, finalize com: "Sessão concluída, sem impedimentos, perfil fechado."`,
  ].join('\n')
}

// =============================================================================
// Fachada única (o que o formulário do site chama)
// =============================================================================

export function buildWarmupPrompt(
  profiles: readonly WarmupProfileInput[],
  options: WarmupOptions = {},
): string {
  const mode: WarmupMode = options.mode ?? 'session'
  return mode === 'verification'
    ? buildVerificationPrompt(profiles, options)
    : buildSessionPrompt(profiles, options)
}

// =============================================================================
// Utilitário original (mantido)
// =============================================================================

/** Localiza o grupo "FB | ESTEIRA DE PERFIS" ignorando maiúsculas, acentos e variações de espaço. */
export function findDefaultGroup<T extends { groupName: string }>(
  groups: readonly T[],
): T | undefined {
  const normalize = (value: string) =>
    value
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toUpperCase()
      .replace(/\s+/g, ' ')
      .trim()

  return groups.find(group => /^FB \| ESTEIRA DE PERFIS?$/.test(normalize(group.groupName)))
}
