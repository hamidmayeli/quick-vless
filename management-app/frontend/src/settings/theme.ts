export type ThemePreference = 'system' | 'light' | 'dark'

const THEME_STORAGE_KEY = 'easy-xray-theme'

export function getThemePreference(): ThemePreference {
  try {
    const preference = localStorage.getItem(THEME_STORAGE_KEY)
    if (preference === 'light' || preference === 'dark') return preference
  } catch {
    return 'system'
  }
  return 'system'
}

export function setThemePreference(preference: ThemePreference) {
  try {
    if (preference === 'system') localStorage.removeItem(THEME_STORAGE_KEY)
    else localStorage.setItem(THEME_STORAGE_KEY, preference)
  } catch {
    // Theme still applies for this session when storage is unavailable.
  }

  if (preference === 'system') document.documentElement.removeAttribute('data-theme')
  else document.documentElement.dataset.theme = preference
}