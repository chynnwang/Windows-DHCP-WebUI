import { defineStore } from 'pinia'
import { ref } from 'vue'

export type ThemeMode = 'light' | 'dark'

const STORAGE_KEY = 'dhcp-theme'

function apply(mode: ThemeMode) {
  const el = document.documentElement
  if (mode === 'dark') el.classList.add('dark')
  else el.classList.remove('dark')
}

function resolveInitial(): ThemeMode {
  // 优先级:URL 参数(父平台传入) > localStorage > 默认浅色
  const url = new URLSearchParams(window.location.search).get('theme')
  if (url === 'dark' || url === 'light') return url
  const saved = localStorage.getItem(STORAGE_KEY)
  if (saved === 'dark' || saved === 'light') return saved
  return 'light'
}

export const useTheme = defineStore('theme', () => {
  const mode = ref<ThemeMode>('light')

  function set(next: ThemeMode, persist = true) {
    mode.value = next
    apply(next)
    if (persist) localStorage.setItem(STORAGE_KEY, next)
  }

  function toggle() {
    set(mode.value === 'dark' ? 'light' : 'dark')
  }

  function init() {
    set(resolveInitial(), false)
    // 父平台可通过 postMessage({ type: 'set-theme', theme: 'dark' }) 实时联动(可选)
    window.addEventListener('message', (e) => {
      const d = e.data
      if (d && d.type === 'set-theme' && (d.theme === 'dark' || d.theme === 'light')) {
        set(d.theme, false)
      }
    })
  }

  return { mode, set, toggle, init }
})
