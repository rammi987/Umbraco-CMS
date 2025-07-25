const { rest } = window.MockServiceWorker;
import { umbUserMockDb } from '../../data/user/user.db.js';
import { UMB_SLUG } from './slug.js';
import type { CreateUserRequestModel, UpdateUserRequestModel } from '@umbraco-cms/backoffice/external/backend-api';
import { umbracoPath } from '@umbraco-cms/backoffice/utils';

export const detailHandlers = [
	rest.post(umbracoPath(`${UMB_SLUG}`), async (req, res, ctx) => {
		const requestBody = (await req.json()) as CreateUserRequestModel;
		if (!requestBody) return res(ctx.status(400, 'no body found'));

		const id = umbUserMockDb.detail.create(requestBody);

		return res(
			ctx.status(201),
			ctx.set({
				Location: req.url.href + '/' + id,
				'Umb-Generated-Resource': id,
			}),
		);
	}),

	rest.get(umbracoPath(`${UMB_SLUG}/configuration`), (_req, res, ctx) => {
		return res(ctx.status(200), ctx.json(umbUserMockDb.getConfiguration()));
	}),

	rest.get(umbracoPath(`${UMB_SLUG}/:id/calculate-start-nodes`), (req, res, ctx) => {
		const id = req.params.id as string;
		if (!id) return res(ctx.status(400));
		if (id === 'forbidden') {
			// Simulate a forbidden response
			return res(ctx.status(403));
		}
		return res(ctx.status(200), ctx.json(umbUserMockDb.calculateStartNodes(id)));
	}),

	rest.get(umbracoPath(`${UMB_SLUG}/:id/client-credentials`), (req, res, ctx) => {
		const id = req.params.id as string;
		if (!id) return res(ctx.status(400));
		if (id === 'forbidden') {
			// Simulate a forbidden response
			return res(ctx.status(403));
		}
		return res(ctx.status(200), ctx.json(umbUserMockDb.clientCredentials(id)));
	}),

	rest.get(umbracoPath(`${UMB_SLUG}/:id`), (req, res, ctx) => {
		const id = req.params.id as string;
		if (!id) return res(ctx.status(400));
		if (id === 'forbidden') {
			// Simulate a forbidden response
			return res(ctx.status(403));
		}
		const response = umbUserMockDb.detail.read(id);
		return res(ctx.status(200), ctx.json(response));
	}),

	rest.put(umbracoPath(`${UMB_SLUG}/:id`), async (req, res, ctx) => {
		const id = req.params.id as string;
		if (!id) return res(ctx.status(400));
		if (id === 'forbidden') {
			// Simulate a forbidden response
			return res(ctx.status(403));
		}
		const requestBody = (await req.json()) as UpdateUserRequestModel;
		if (!requestBody) return res(ctx.status(400, 'no body found'));
		umbUserMockDb.detail.update(id, requestBody);
		return res(ctx.status(200));
	}),

	rest.delete(umbracoPath(`${UMB_SLUG}/:id`), (req, res, ctx) => {
		const id = req.params.id as string;
		if (!id) return res(ctx.status(400));
		if (id === 'forbidden') {
			// Simulate a forbidden response
			return res(ctx.status(403));
		}
		umbUserMockDb.detail.delete(id);
		return res(ctx.status(200));
	}),
];
