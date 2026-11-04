<script lang="ts">
	import { getContext, tick } from 'svelte';
	import { marked } from 'marked';
	// @ts-ignore
	import DOMPurify from 'dompurify';

	import { chatWithAgent, type AgentDefinition, type TraceEvent } from '$lib/apis/a2a';
	import Spinner from '$lib/components/common/Spinner.svelte';

	const i18n = getContext('i18n');

	export let agent: AgentDefinition;
	export let agentNames: Record<string, string> = {};

	let input = '';
	let loading = false;
	let error = '';
	let messages: { role: 'user' | 'assistant'; content: string; events?: TraceEvent[] }[] = [];
	let container: HTMLDivElement;

	const label = (id: string) =>
		id?.startsWith('gebruiker')
			? $i18n.t('Gebruiker')
			: (agentNames[id] ?? id?.replace(/^remote:/, '') ?? '?');

	// Alleen het verkeer tussen agents tonen; request/response op diepte 0 is het gesprek zelf.
	const agentTraffic = (events: TraceEvent[] = []) =>
		events.filter(
			(e) => e.type === 'delegation' || e.type === 'delegation_result' || e.type === 'error'
		);

	const render = (text: string) => DOMPurify.sanitize(marked.parse(text ?? '') as string);

	const send = async () => {
		const text = input.trim();
		if (!text || loading) return;

		input = '';
		error = '';
		messages = [...messages, { role: 'user', content: text }];
		loading = true;
		await tick();
		container?.scrollTo({ top: container.scrollHeight });

		try {
			const res = await chatWithAgent(
				localStorage.token,
				agent.id,
				messages.map((m) => ({ role: m.role, content: m.content }))
			);
			messages = [
				...messages,
				{ role: 'assistant', content: res.content, events: res.trace?.events ?? [] }
			];
		} catch (e) {
			error = `${e}`;
		}

		loading = false;
		await tick();
		container?.scrollTo({ top: container.scrollHeight });
	};
</script>

<div
	class="flex flex-col rounded-xl border border-gray-100 dark:border-gray-850 max-w-3xl mx-auto mb-10"
>
	<div
		class="flex items-center justify-between px-4 py-2 border-b border-gray-100 dark:border-gray-850"
	>
		<div class="text-sm font-semibold">{$i18n.t('Test deze agent')}</div>
		{#if messages.length > 0}
			<button
				class="text-xs text-gray-500 hover:underline"
				type="button"
				on:click={() => {
					messages = [];
					error = '';
				}}>{$i18n.t('Nieuw gesprek')}</button
			>
		{/if}
	</div>

	<div class="flex flex-col gap-3 p-4 max-h-[32rem] overflow-y-auto text-sm" bind:this={container}>
		{#if messages.length === 0}
			<div class="text-xs text-gray-500">
				{$i18n.t(
					'Stel een vraag. Als de agent collega-agents inschakelt, zie je hieronder per antwoord welke vragen de agents elkaar hebben gesteld.'
				)}
			</div>
		{/if}

		{#each messages as message}
			{#if message.role === 'user'}
				<div
					class="self-end max-w-[85%] rounded-2xl bg-gray-50 dark:bg-gray-850 px-3 py-2 whitespace-pre-wrap"
				>
					{message.content}
				</div>
			{:else}
				<div class="flex flex-col gap-2">
					{#if agentTraffic(message.events).length > 0}
						<details
							class="rounded-lg border border-dashed border-gray-200 dark:border-gray-800 px-3 py-2 text-xs"
						>
							<summary class="cursor-pointer text-gray-500">
								{$i18n.t('Overleg tussen agents')} ({agentTraffic(message.events).filter(
									(e) => e.type === 'delegation'
								).length})
							</summary>
							<div class="flex flex-col gap-2 mt-2">
								{#each agentTraffic(message.events) as event}
									<div style="margin-left: {(Math.max(event.depth, 1) - 1) * 1}rem">
										<div class="font-medium {event.type === 'error' ? 'text-red-600' : ''}">
											{label(event.from)}
											<span class="text-gray-400">
												{event.type === 'delegation'
													? '→ vraagt'
													: event.type === 'error'
														? '⚠'
														: '→ antwoordt'}
											</span>
											{label(event.to)}
										</div>
										<div class="text-gray-600 dark:text-gray-400 whitespace-pre-wrap line-clamp-6">
											{event.content}
										</div>
									</div>
								{/each}
							</div>
						</details>
					{/if}
					<div class="markdown-prose max-w-full">
						{@html render(message.content)}
					</div>
				</div>
			{/if}
		{/each}

		{#if loading}
			<div class="flex items-center gap-2 text-xs text-gray-500">
				<Spinner className="size-4" />
				{$i18n.t('Agent denkt na en overlegt mogelijk met collega-agents…')}
			</div>
		{/if}

		{#if error}
			<div class="text-xs text-red-600">{error}</div>
		{/if}
	</div>

	<form
		class="flex gap-2 p-3 border-t border-gray-100 dark:border-gray-850"
		on:submit|preventDefault={send}
	>
		<input
			class="flex-1 rounded-lg py-2 px-3 text-sm bg-gray-50 dark:bg-gray-850 outline-hidden"
			placeholder={$i18n.t('Send a Message')}
			bind:value={input}
			disabled={loading || !agent.enabled}
		/>
		<button
			class="px-3 py-2 rounded-lg text-sm bg-black text-white dark:bg-white dark:text-black disabled:opacity-50"
			type="submit"
			disabled={loading || !input.trim() || !agent.enabled}>{$i18n.t('Send')}</button
		>
	</form>
</div>
