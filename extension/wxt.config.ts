import { defineConfig } from 'wxt';

// WXT auto-discovers entrypoints/ and generates the MV3 manifest.
// https://wxt.dev
export default defineConfig({
  modules: ['@wxt-dev/module-react'],
  manifest: {
    name: 'SynapseAI — RAG Assistant',
    description:
      'Hyper-modern AI on any page: select text to summarize, explain, translate, rewrite, or ask questions grounded in the page via RAG.',
    permissions: ['storage', 'activeTab'],
    host_permissions: [
      'https://api-distributed-rag.azurewebsites.net/*',
      'https://localhost:7295/*',
      'http://localhost:5216/*',
    ],
    icons: {
      16: '/icons/icon16.png',
      48: '/icons/icon48.png',
      128: '/icons/icon128.png',
    },
    action: {
      default_title: 'SynapseAI',
      default_icon: {
        16: '/icons/icon16.png',
        48: '/icons/icon48.png',
        128: '/icons/icon128.png',
      },
    },
  },
});
