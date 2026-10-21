import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
  site: 'https://adofai-ipc.impl1113.dev',
  base: '/docs',
  trailingSlash: 'always',
  integrations: [
    starlight({
      title: 'ADOFAI IPC',
      description: 'ADOFAI IPC 개발 문서',
      favicon: '/favicon.svg',
      disable404Route: true,
      locales: { root: { label: '한국어', lang: 'ko' } },
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/KGH1113/adofai-ipc' },
      ],
      customCss: ['./src/styles/custom.css'],
      sidebar: [
        { label: '소개', slug: '' },
        { label: '계약 정의', slug: 'learn/contract' },
        { label: '모드 API', slug: 'reference/mod' },
        { label: '앱 API', slug: 'reference/app' },
        { label: '파일 전송', slug: 'reference/files' },
        { label: '코드 생성과 저장소 구성', slug: 'guides/monorepo' },
        { label: '동작 규칙', slug: 'reference/lifecycle' },
        { label: '예제 프로젝트', items: [
          { label: '모드 설정 패널', slug: 'guides/settings-example' },
          { label: '1. 프로젝트와 계약 준비', slug: 'examples/settings/setup' },
          { label: '2. 모드 등록', slug: 'examples/settings/mod' },
          { label: '3. 브라우저에서 조회', slug: 'examples/settings/read' },
          { label: '4. 저장 기능 추가', slug: 'examples/settings/write' },
        ] },
      ],
      expressiveCode: { themes: ['github-dark', 'github-light'], useThemedScrollbars: false },
      tableOfContents: { minHeadingLevel: 2, maxHeadingLevel: 3 },
      pagination: true,
      lastUpdated: false,
      editLink: {
        baseUrl: 'https://github.com/KGH1113/adofai-ipc/edit/main/docs/',
      },
    }),
  ],
});
