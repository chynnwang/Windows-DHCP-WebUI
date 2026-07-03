import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api } from '../api'

export type Role = 'Admin' | 'Viewer'

export interface AuthUser {
  username: string
  role: Role
}

const TOKEN_KEY = 'dhcp-token'

export const useAuth = defineStore('auth', () => {
  const token = ref<string>(localStorage.getItem(TOKEN_KEY) ?? '')
  const user = ref<AuthUser | null>(null)

  const isAuthenticated = computed(() => !!token.value)
  const isAdmin = computed(() => user.value?.role === 'Admin')

  function setToken(next: string) {
    token.value = next
    if (next) localStorage.setItem(TOKEN_KEY, next)
    else localStorage.removeItem(TOKEN_KEY)
  }

  async function login(username: string, password: string) {
    const { data } = await api.login(username, password)
    setToken(data.token)
    user.value = { username: data.username, role: data.role as Role }
    return data
  }

  async function loadMe() {
    const { data } = await api.me()
    user.value = { username: data.username, role: data.role as Role }
    return user.value
  }

  function logout() {
    setToken('')
    user.value = null
  }

  return { token, user, isAuthenticated, isAdmin, setToken, login, loadMe, logout }
})
