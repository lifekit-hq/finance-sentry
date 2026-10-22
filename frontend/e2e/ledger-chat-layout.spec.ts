import {expect, type Page, test} from '@playwright/test';

import {API} from './support/dossier-mocks';
import {mockGet, mockShell} from './support/visual-baseline';

function message(id: string, role: string, content: string) {
  return {
    id,
    role,
    content,
    toolCallsJson: null,
    toolResultsJson: null,
    createdAt: '2026-09-14T18:00:00Z',
  };
}

const CONVERSATION = {
  id: 'conv-1',
  title: 'Allocation drift this quarter',
  updatedAt: '2026-09-14T18:00:00Z',
  modelId: 'claude-sonnet-5-5',
};
const PAIRS = 8;
const LAST_QUESTION = `Question ${PAIRS}: how is my allocation drifting?`;
const REPLY_TEXT = 'Drift is within band.';
const STREAM_DELAY_MS = 1500;
const ANSWER =
  'Equities are 71% of the book against a 65% target, so the drift is +6 points. '.repeat(2);
const MESSAGES = Array.from({length: PAIRS}, (_, i) => [
  message(`u${i}`, 'user', `Question ${i + 1}: how is my allocation drifting?`),
  message(`a${i}`, 'assistant', `**Hold.** ${ANSWER}`),
]).flat();

const PHONES = {
  'iPhone 390x844': {width: 390, height: 844},
  'small phone 320x568': {width: 320, height: 568},
} as const;
const DESKTOP = {width: 1440, height: 900};

async function openThread(page: Page, isPhone: boolean): Promise<void> {
  await mockShell(page);
  await mockGet(page, 'agent/conversations', [CONVERSATION]);
  await page.route(`${API}/agent/conversations/conv-1`, route =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({...CONVERSATION, createdAt: 'x', messages: MESSAGES}),
    })
  );
  await page.route(`${API}/agent/chat`, async route => {
    await new Promise(resolve => setTimeout(resolve, STREAM_DELAY_MS));
    await route.fulfill({
      contentType: 'text/event-stream',
      body: `event: text\ndata: {"delta":"${REPLY_TEXT}"}\n\nevent: done\ndata: {"messageId":"m1"}\n\n`,
    });
  });
  await page.goto('/ledger');
  if (isPhone) {
    await page.getByRole('button', {name: 'Sessions'}).click();
    await page.getByRole('button', {name: CONVERSATION.title}).click();
  } else {
    await page.locator('aside[aria-label="Conversations"]').getByText(CONVERSATION.title).click();
  }
  await expect(page.getByText(LAST_QUESTION)).toBeVisible();
}

/** Every element that scrolls its own content, outside the chat's shadow root. */
async function scrollingElements(page: Page): Promise<string[]> {
  return page.evaluate(() =>
    [...document.querySelectorAll('*')]
      .filter(
        el =>
          /(auto|scroll)/.test(getComputedStyle(el).overflowY) &&
          el.scrollHeight > el.clientHeight + 1
      )
      .map(el => `${el.tagName.toLowerCase()}.${el.className}`)
  );
}

async function composerBox(page: Page) {
  const box = await page.locator('deep-chat #text-input').boundingBox();
  expect(box).not.toBeNull();
  return box as NonNullable<typeof box>;
}

const SIZES = {...PHONES, 'desktop 1440x900': DESKTOP};

for (const [name, viewport] of Object.entries(SIZES)) {
  const isPhone = viewport.width < DESKTOP.width;
  test.describe(`ledger chat layout (${name})`, () => {
    test.use({viewport, hasTouch: isPhone, isMobile: isPhone});

    test('the page never scrolls: only the message list does', async ({page}) => {
      await openThread(page, isPhone);

      await expect.poll(() => scrollingElements(page)).toEqual([]);
      const scrolled = await page.evaluate(() => window.scrollY);
      expect(scrolled).toBe(0);
    });

    test('the composer stays pinned inside the viewport', async ({page}) => {
      await openThread(page, isPhone);

      const box = await composerBox(page);
      expect(box.y + box.height).toBeLessThanOrEqual(viewport.height);
      if (isPhone) {
        const tabBar = await page.locator('cmn-bottom-tab-bar nav').boundingBox();
        expect(box.y + box.height).toBeLessThanOrEqual((tabBar as NonNullable<typeof tabBar>).y);
      }
    });

    test('an opened thread lands on its newest message', async ({page}) => {
      await openThread(page, isPhone);

      await expect(page.getByText(LAST_QUESTION)).toBeInViewport();
    });

    test('Enter sends, the reply is pending, then lands on the newest message', async ({page}) => {
      await openThread(page, isPhone);

      await page.locator('deep-chat #text-input').click();
      await page.keyboard.type('Should I trim equities?');
      await page.keyboard.press('Enter');

      await expect(page.getByText('Should I trim equities?')).toBeInViewport();
      await expect(
        page.locator('deep-chat .deep-chat-loading-message-dots-container')
      ).toBeVisible();
      await expect(page.getByText(REPLY_TEXT)).toBeInViewport();
      await expect.poll(() => scrollingElements(page)).toEqual([]);
    });
  });
}
