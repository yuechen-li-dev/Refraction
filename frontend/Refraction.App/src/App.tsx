import { Navigate, Route, Routes } from 'react-router-dom'
import { HostPage } from './pages/HostPage'
import { ViewerPage } from './pages/ViewerPage'

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<HostPage />} />
      <Route path="/r/:slug" element={<ViewerPage />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
