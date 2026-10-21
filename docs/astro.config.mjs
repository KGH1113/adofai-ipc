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
      defaultLocale: 'root',
      locales: {
        root: { label: '한국어', lang: 'ko' },
        en: { label: 'English', lang: 'en' },
        'zh-cn': { label: '简体中文', lang: 'zh-CN' },
      },
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/KGH1113/adofai-ipc' },
      ],
      customCss: ['./src/styles/custom.css'],
      sidebar: [
        { label: '소개', translations: { en: 'Introduction', 'zh-CN': '简介' }, slug: '' },
        { label: '계약 정의', translations: { en: 'Contract definitions', 'zh-CN': '契约定义' }, slug: 'learn/contract' },
        { label: '모드 API', translations: { en: 'Mod API', 'zh-CN': '模组 API' }, slug: 'reference/mod' },
        { label: '앱 API', translations: { en: 'App API', 'zh-CN': '应用 API' }, slug: 'reference/app' },
        { label: '파일 전송', translations: { en: 'File transfers', 'zh-CN': '文件传输' }, slug: 'reference/files' },
        { label: '코드 생성과 저장소 구성', translations: { en: 'Code generation and repositories', 'zh-CN': '代码生成与仓库结构' }, slug: 'guides/monorepo' },
        { label: '동작 규칙', translations: { en: 'Runtime rules', 'zh-CN': '运行规则' }, slug: 'reference/lifecycle' },
        { label: '예제 프로젝트', translations: { en: 'Example project', 'zh-CN': '示例项目' }, items: [
          { label: '모드 설정 패널', translations: { en: 'Mod settings panel', 'zh-CN': '模组设置面板' }, slug: 'guides/settings-example' },
          { label: '1. 프로젝트와 계약 준비', translations: { en: '1. Project and contract', 'zh-CN': '1. 准备项目与契约' }, slug: 'examples/settings/setup' },
          { label: '2. 모드 등록', translations: { en: '2. Register the mod', 'zh-CN': '2. 注册模组' }, slug: 'examples/settings/mod' },
          { label: '3. 브라우저에서 조회', translations: { en: '3. Read in the browser', 'zh-CN': '3. 在浏览器中读取' }, slug: 'examples/settings/read' },
          { label: '4. 저장 기능 추가', translations: { en: '4. Add saving', 'zh-CN': '4. 添加保存功能' }, slug: 'examples/settings/write' },
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
