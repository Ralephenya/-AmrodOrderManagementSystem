import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Loader2 } from 'lucide-react'
import { useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { z } from 'zod'
import { ApiError } from '@/api/problem'
import { useCountries, useCreateCustomer } from '@/api/queries'
import { Field } from '@/components/Field'
import { PageHeader } from '@/components/PageHeader'
import { ProblemAlert } from '@/components/ProblemAlert'
import { useToast } from '@/components/useToast'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { NativeSelect } from '@/components/ui/native-select'
import { applyServerErrors } from '@/lib/forms'

// Mirrors the API's CreateCustomerRequestValidator, so most mistakes are caught before a round trip.
// The API stays the authority: anything it rejects is shown on the matching field.
const schema = z.object({
  name: z.string().trim().min(1, "Please enter the customer's name.").max(200, 'Name can be at most 200 characters.'),
  email: z
    .string()
    .trim()
    .min(1, "Please enter the customer's email address.")
    .max(320, 'Email can be at most 320 characters.')
    .pipe(z.email('Please enter a valid email address, like name@example.co.za.')),
  countryCode: z.string().min(1, "Please choose the customer's country."),
})

type FormValues = z.infer<typeof schema>

export function CreateCustomerPage() {
  const countries = useCountries()
  const create = useCreateCustomer()
  const navigate = useNavigate()
  const toast = useToast()
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', email: '', countryCode: '' },
  })
  const { errors, isSubmitting } = form.formState
  const countryCode = useWatch({ control: form.control, name: 'countryCode' })
  const country = countries.data?.find((c) => c.code === countryCode)

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const customer = await create.mutateAsync(values)
      toast.success(`Customer ${customer.name} created.`)
      await navigate('/customers')
    } catch (error) {
      // 409 email_already_exists belongs on the email field; validation errors map onto their fields.
      if (error instanceof ApiError && error.code === 'email_already_exists') {
        form.setError('email', { message: error.detail ?? error.title })
        return
      }
      if (!applyServerErrors(error, form.setError, ['name', 'email', 'countryCode'])) {
        toast.error(error instanceof ApiError ? error.title : "Couldn't create the customer.")
      }
    }
  })

  return (
    <section aria-labelledby="new-customer-title" className="mx-auto max-w-2xl">
      <Button asChild variant="ghost" size="sm" className="mb-4 -ml-2 text-muted-foreground">
        <Link to="/customers">
          <ArrowLeft />
          Customers
        </Link>
      </Button>
      <PageHeader titleId="new-customer-title" title="New customer" description="Add a business so it can place orders." />

      {countries.isError && <ProblemAlert error={countries.error} onRetry={() => void countries.refetch()} />}

      <form onSubmit={(e) => void onSubmit(e)} noValidate>
        <Card>
          <CardHeader>
            <CardTitle>Customer details</CardTitle>
            <CardDescription>The email must be unique. The country decides which currencies they can order in.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-5">
            <Field label="Name" error={errors.name?.message}>
              <Input type="text" autoComplete="organization" placeholder="Etosha Traders" {...form.register('name')} />
            </Field>
            <Field label="Email" error={errors.email?.message}>
              <Input type="email" autoComplete="email" placeholder="orders@example.co.za" {...form.register('email')} />
            </Field>
            <Field
              label="Country"
              error={errors.countryCode?.message}
              hint={
                country
                  ? `Can order in ${country.currencies.map((c) => c.code).join(' or ')}.`
                  : 'SADC member states only.'
              }
            >
              <NativeSelect disabled={!countries.data} {...form.register('countryCode')}>
                <option value="">{countries.isPending ? 'Loading countries…' : 'Choose a country'}</option>
                {countries.data?.map((c) => (
                  <option key={c.code} value={c.code}>
                    {c.name}
                  </option>
                ))}
              </NativeSelect>
            </Field>
          </CardContent>
          <CardFooter className="justify-end gap-2 border-t">
            <Button asChild variant="outline">
              <Link to="/customers">Cancel</Link>
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="animate-spin" />}
              {isSubmitting ? 'Creating…' : 'Create customer'}
            </Button>
          </CardFooter>
        </Card>
      </form>
    </section>
  )
}
